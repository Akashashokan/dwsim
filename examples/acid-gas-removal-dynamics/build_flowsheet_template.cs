using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DWSIM.Automation;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;
using DWSIM.Interfaces.Enums.GraphicObjects;
using DWSIM.DynamicsManager;
using DWSIM.FlowsheetSolver;
using DWSIM.UnitOperations.UnitOperations;
using DWSIM.UnitOperations.UnitOperations.Auxiliary;
using DWSIM.Thermodynamics.PropertyPackages;

// Acid gas removal surrogate flowsheet.
//
// Design philosophy (replaces the previous rigorous AbsorptionColumn approach):
//
//   Absorber surrogate  — ComponentSeparator with fixed molar-recovery splits.
//                         Avoids all Sum-Rates / Naphtali-Sandholm convergence
//                         issues while preserving the correct process topology.
//
//   Regenerator surrogate — Heater (150 °C) + Flash Vessel.
//                           At 35 bar / 150 °C, CO2 (Tc 304 K) and H2S (Tc 373 K)
//                           are supercritical, K >> 1, so they flash to vapour;
//                           water and amine remain liquid.  No pressure-reduction
//                           valve is needed; regeneration at absorber pressure
//                           also means no repressurisation pump head is needed.
//
//   Property package    — single Peng-Robinson instance for all objects.
//                         Eliminates the electrolyte-flash divergence that occurred
//                         when Amines/NRTL was assigned to the gas-side streams.
//
// Flowsheet topology:
//   Feed → FeedSep → DryFeedGas → AbsorberSurrogate ─(outlet 0)→ SweetGas
//                                                    └(outlet 1)→ AbsorbedComps
//   AbsorbedComps + LeanAmineRecycle → RichMixer → RichAmine
//   RichAmine → RegenHeater → HotRichAmine → RegenFlash
//      ├─(vapor)→ AcidGasProduct
//      └─(liq)→  HotLeanAmine → LeanCooler → CooledLeanAmine
//                → LeanPump → PumpedLeanAmine → AmineRecycle → LeanAmineRecycle
//   SweetGas → GasGasHX → CooledSweetGas → SalesGasSep → SalesGas

public static class AcidGasRemovalDynamicTemplate
{
    public static void Generate(string outputFile)
    {
        var automation = new Automation3();
        var sim = automation.CreateFlowsheet();

        // -----------------------------------------------------------------------
        // Compounds
        // -----------------------------------------------------------------------
        AddCompoundOrThrow(sim, "Methane");
        AddCompoundOrThrow(sim, "Carbon dioxide");
        AddCompoundOrThrow(sim, "Hydrogen sulfide");
        AddCompoundOrThrow(sim, "Water");
        AddCompoundIfAvailable(sim, "Nitrogen");
        AddCompoundIfAvailable(sim, "Ethane");
        AddCompoundIfAvailable(sim, "Propane");

        var amineName = AddFirstAvailableCompound(sim, new[]
        {
            "Methyl diethanolamine",
            "Monoethanolamine",
            "Diethanolamine"
        });
        if (amineName == null)
            throw new Exception("Could not add any amine compound (MDEA/MEA/DEA).");

        // -----------------------------------------------------------------------
        // Single Peng-Robinson property package for the whole flowsheet.
        // -----------------------------------------------------------------------
        var ppName = sim.GetAvailablePropertyPackages()
            .FirstOrDefault(x => { var l = x.ToLowerInvariant();
                                   return l.Contains("peng") && l.Contains("robinson"); })
            ?? sim.GetAvailablePropertyPackages().FirstOrDefault();
        if (ppName == null)
            throw new Exception("No property package is available.");

        var pp  = sim.CreateAndAddPropertyPackage(ppName);
        var ppc = (PropertyPackage)(object)pp;

        // -----------------------------------------------------------------------
        // Simulation objects
        // -----------------------------------------------------------------------

        // Feed path
        var feed            = sim.AddObject(ObjectType.MaterialStream,    40,  200, "Feed");
        var feedSeparator   = sim.AddObject(ObjectType.Vessel,           160,  200, "Feed water sep.");
        var feedSepLiquid   = sim.AddObject(ObjectType.MaterialStream,   160,  310, "Feed sep. water");
        var feedSepLiquid2  = sim.AddObject(ObjectType.MaterialStream,   160,  360, "Feed sep. liquid 2"); // Vessel connector 2
        var absFeed         = sim.AddObject(ObjectType.MaterialStream,   280,  180, "Dry feed gas");

        // Absorber surrogate
        var absorberSurr   = sim.AddObject(ObjectType.ComponentSeparator,380,  200, "ABSORBER SURROGATE");
        var sweetGas       = sim.AddObject(ObjectType.MaterialStream,    500,  150, "Sweet gas");
        var absorbedComps  = sim.AddObject(ObjectType.MaterialStream,    500,  260, "Absorbed acid comps");

        // Sales gas path
        var richCooler      = sim.AddObject(ObjectType.Cooler,           610,  150, "Gas-gas HX");
        var coolRichGas     = sim.AddObject(ObjectType.MaterialStream,   730,  150, "Cooled sweet gas");
        var salesSeparator  = sim.AddObject(ObjectType.Vessel,           840,  150, "Sales gas water sep");
        var salesGas        = sim.AddObject(ObjectType.MaterialStream,   960,  130, "Sales gas");
        var salesSepLiquid  = sim.AddObject(ObjectType.MaterialStream,   840,  260, "Sales gas condensate");
        var salesSepLiquid2 = sim.AddObject(ObjectType.MaterialStream,   840,  310, "Sales gas liquid 2"); // Vessel connector 2

        // Rich amine: mix absorbed components with lean amine recycle
        var richMixer      = sim.AddObject(ObjectType.Mixer,             610,  270, "Rich amine mixer");
        var richAmine      = sim.AddObject(ObjectType.MaterialStream,    730,  270, "Rich amine");

        // Regeneration surrogate: Heater + Flash
        var regenHeater    = sim.AddObject(ObjectType.Heater,            840,  270, "Regen heater");
        var hotRichAmine   = sim.AddObject(ObjectType.MaterialStream,    960,  270, "Hot rich amine");
        var regenFlash      = sim.AddObject(ObjectType.Vessel,          1070,  270, "Regen flash sep.");
        var acidicGas       = sim.AddObject(ObjectType.MaterialStream,  1070,  160, "Acid gas product");
        var hotLeanAmine    = sim.AddObject(ObjectType.MaterialStream,  1070,  380, "Hot lean amine");
        var regenFlashLiq2  = sim.AddObject(ObjectType.MaterialStream,  1070,  430, "Regen flash liquid 2"); // Vessel connector 2

        // Lean amine recirculation
        // leanSeparator: flash vessel between the cooler and the pump.
        // After cooling to 40 °C at 35 bar, residual dissolved CO2/H2S can form
        // a small vapour fraction that would trigger "vapor phase detected at pump
        // inlet".  The separator routes that vapour to a vent sink and feeds only
        // the liquid phase to the pump.
        var leanCooler     = sim.AddObject(ObjectType.Cooler,           1180,  380, "Lean amine cooler");
        var coolLeanAmine  = sim.AddObject(ObjectType.MaterialStream,   1300,  380, "Cooled lean amine");
        var leanSeparator   = sim.AddObject(ObjectType.Vessel,          1400,  380, "Lean amine sep.");
        var leanSepVapor    = sim.AddObject(ObjectType.MaterialStream,  1400,  280, "Lean sep. vent");
        var leanSepLiquid   = sim.AddObject(ObjectType.MaterialStream,  1520,  380, "Liquid lean amine");
        var leanSepLiquid2  = sim.AddObject(ObjectType.MaterialStream,  1400,  460, "Lean sep. liquid 2"); // Vessel connector 2
        var leanPump       = sim.AddObject(ObjectType.Pump,             1620,  380, "Lean amine pump");
        var pumpedLeanAmine= sim.AddObject(ObjectType.MaterialStream,   1740,  380, "Pumped lean amine");
        var amineRecycle   = sim.AddObject(ObjectType.OT_Recycle,       1840,  380, "Amine recycle");
        var recycleToAbs   = sim.AddObject(ObjectType.MaterialStream,    610,  330, "Lean amine (recycle)");

        foreach (var o in sim.SimulationObjects.Values)
            ((dynamic)o.GraphicObject).PositionConnectors();

        // -----------------------------------------------------------------------
        // Connections
        // -----------------------------------------------------------------------

        // Feed path
        sim.ConnectObjects(feed.GraphicObject,            feedSeparator.GraphicObject,   0, 0);
        sim.ConnectObjects(feedSeparator.GraphicObject,   absFeed.GraphicObject,         0, 0); // vapor
        sim.ConnectObjects(feedSeparator.GraphicObject,   feedSepLiquid.GraphicObject,   1, 0); // liquid 1
        sim.ConnectObjects(feedSeparator.GraphicObject,   feedSepLiquid2.GraphicObject,  2, 0); // liquid 2 (three-phase guard)

        // Absorber surrogate
        // OutputConnector(0) = outstr1 = sweetGas
        // OutputConnector(1) = outstr2 = absorbedComps (SpecifiedStreamIndex=1 → outstr2 is specified)
        sim.ConnectObjects(absFeed.GraphicObject,        absorberSurr.GraphicObject,    0, 0);
        sim.ConnectObjects(absorberSurr.GraphicObject,   sweetGas.GraphicObject,        0, 0);
        sim.ConnectObjects(absorberSurr.GraphicObject,   absorbedComps.GraphicObject,   1, 0);

        // Sales gas path
        sim.ConnectObjects(sweetGas.GraphicObject,       richCooler.GraphicObject,      0, 0);
        sim.ConnectObjects(richCooler.GraphicObject,     coolRichGas.GraphicObject,     0, 0);
        sim.ConnectObjects(coolRichGas.GraphicObject,    salesSeparator.GraphicObject,  0, 0);
        sim.ConnectObjects(salesSeparator.GraphicObject,  salesGas.GraphicObject,        0, 0);
        sim.ConnectObjects(salesSeparator.GraphicObject,  salesSepLiquid.GraphicObject,  1, 0); // liquid 1
        sim.ConnectObjects(salesSeparator.GraphicObject,  salesSepLiquid2.GraphicObject, 2, 0); // liquid 2 (three-phase guard)

        // Rich amine formation: absorbed comps + lean amine recycle → rich amine
        sim.ConnectObjects(absorbedComps.GraphicObject,  richMixer.GraphicObject,       0, 0); // mixer input 0
        sim.ConnectObjects(recycleToAbs.GraphicObject,   richMixer.GraphicObject,       0, 1); // mixer input 1
        sim.ConnectObjects(richMixer.GraphicObject,      richAmine.GraphicObject,       0, 0);

        // Regeneration
        sim.ConnectObjects(richAmine.GraphicObject,      regenHeater.GraphicObject,     0, 0);
        sim.ConnectObjects(regenHeater.GraphicObject,    hotRichAmine.GraphicObject,    0, 0);
        sim.ConnectObjects(hotRichAmine.GraphicObject,   regenFlash.GraphicObject,      0, 0);
        sim.ConnectObjects(regenFlash.GraphicObject,      acidicGas.GraphicObject,       0, 0); // vapor
        sim.ConnectObjects(regenFlash.GraphicObject,      hotLeanAmine.GraphicObject,    1, 0); // liquid 1
        sim.ConnectObjects(regenFlash.GraphicObject,      regenFlashLiq2.GraphicObject,  2, 0); // liquid 2 (three-phase guard)

        // Lean amine recirculation
        sim.ConnectObjects(hotLeanAmine.GraphicObject,   leanCooler.GraphicObject,      0, 0);
        sim.ConnectObjects(leanCooler.GraphicObject,     coolLeanAmine.GraphicObject,   0, 0);
        sim.ConnectObjects(coolLeanAmine.GraphicObject,  leanSeparator.GraphicObject,   0, 0);
        sim.ConnectObjects(leanSeparator.GraphicObject,   leanSepVapor.GraphicObject,    0, 0); // vapor vent
        sim.ConnectObjects(leanSeparator.GraphicObject,   leanSepLiquid.GraphicObject,   1, 0); // liquid 1
        sim.ConnectObjects(leanSeparator.GraphicObject,   leanSepLiquid2.GraphicObject,  2, 0); // liquid 2 (three-phase guard)
        sim.ConnectObjects(leanSepLiquid.GraphicObject,  leanPump.GraphicObject,        0, 0);
        sim.ConnectObjects(leanPump.GraphicObject,       pumpedLeanAmine.GraphicObject, 0, 0);
        sim.ConnectObjects(pumpedLeanAmine.GraphicObject,amineRecycle.GraphicObject,    0, 0);
        sim.ConnectObjects(amineRecycle.GraphicObject,   recycleToAbs.GraphicObject,    0, 0);

        // -----------------------------------------------------------------------
        // Assign Peng-Robinson to every simulation object.
        // OT_Recycle does not expose PropertyPackage; wrap in try so other
        // objects are not skipped if one assignment fails.
        // -----------------------------------------------------------------------
        foreach (var obj in new ISimulationObject[] {
            feed, feedSeparator, feedSepLiquid, feedSepLiquid2, absFeed,
            absorberSurr, sweetGas, absorbedComps,
            richCooler, coolRichGas, salesSeparator, salesGas, salesSepLiquid, salesSepLiquid2,
            richMixer, richAmine,
            regenHeater, hotRichAmine, regenFlash, acidicGas, hotLeanAmine, regenFlashLiq2,
            leanCooler, coolLeanAmine,
            leanSeparator, leanSepVapor, leanSepLiquid, leanSepLiquid2,
            leanPump, pumpedLeanAmine,
            amineRecycle, recycleToAbs })
        {
            try { ((dynamic)obj).PropertyPackage = ppc; } catch { }
        }

        // -----------------------------------------------------------------------
        // ComponentSeparator specs
        //
        // SpecifiedStreamIndex = 1 → OutputConnector(1) = absorbedComps is the
        // "specified" stream.  Each component spec gives the percentage of the
        // inlet molar flow that routes to absorbedComps.
        // Components with no spec default to 0 % (100 % stays in sweetGas).
        // -----------------------------------------------------------------------
        var cs = (ComponentSeparator)(object)absorberSurr;
        cs.SpecifiedStreamIndex = 1;

        void AddSpec(string compId, double pct)
        {
            cs.ComponentSepSpecs[compId] =
                new ComponentSeparationSpec(compId, SeparationSpec.PercentInletMolarFlow, pct, "");
        }

        AddSpec("Hydrogen sulfide", 90.0); // 90 % H2S absorbed into rich amine
        AddSpec("Carbon dioxide",   70.0); // 70 % CO2 absorbed
        AddSpec("Water",            30.0); // 30 % water co-absorbed (carry solvent)
        // Methane, Nitrogen, Ethane, Propane: no spec → 100 % to sweetGas
        // amineName is not present in absFeed (enters only via lean amine recycle)

        // -----------------------------------------------------------------------
        // Unit operation specifications
        // -----------------------------------------------------------------------

        // Gas-gas HX: cool sweet gas before sales separator
        var richCoolerCast = (Cooler)(object)richCooler;
        richCoolerCast.CalcMode = Cooler.CalculationMode.OutletTemperature;
        richCoolerCast.OutletTemperature = 305.15; // 32 °C

        // Regeneration heater: raise temperature to strip CO2/H2S
        // At 150 °C / 35 bar both CO2 and H2S are supercritical → K >> 1 → flash to vapour
        var regenHeaterCast = (Heater)(object)regenHeater;
        regenHeaterCast.CalcMode = Heater.CalculationMode.OutletTemperature;
        regenHeaterCast.OutletTemperature = 423.15; // 150 °C

        // Lean amine cooler: return lean amine to absorber inlet temperature
        var leanCoolerCast = (Cooler)(object)leanCooler;
        leanCoolerCast.CalcMode = Cooler.CalculationMode.OutletTemperature;
        leanCoolerCast.OutletTemperature = 313.15; // 40 °C

        // Lean amine pump: small DP for line-loss recovery (regeneration and
        // absorber run at the same pressure, so no large head is required)
        ((dynamic)leanPump).DeltaP = 50_000.0; // 0.5 bar

        // -----------------------------------------------------------------------
        // Stream initial conditions (SI units)
        // -----------------------------------------------------------------------

        ((dynamic)feed).SetTemperature(313.15);
        ((dynamic)feed).SetPressure(3_500_000.0);
        ((dynamic)feed).SetMassFlow(2.0);
        ApplyComposition(feed, new Dictionary<string, double>
        {
            ["Methane"]           = 0.850,
            ["Carbon dioxide"]    = 0.080,
            ["Hydrogen sulfide"]  = 0.030,
            ["Water"]             = 0.020,
            ["Nitrogen"]          = 0.010,
            ["Ethane"]            = 0.005,
            ["Propane"]           = 0.002,
        });

        // absFeed seeded so ComponentSeparator has a valid inlet on the very first
        // solver pass (before feedSeparator has propagated its results).
        ((dynamic)absFeed).SetTemperature(313.15);
        ((dynamic)absFeed).SetPressure(3_500_000.0);
        ((dynamic)absFeed).SetMassFlow(2.0);
        ApplyComposition(absFeed, new Dictionary<string, double>
        {
            ["Methane"]           = 0.850,
            ["Carbon dioxide"]    = 0.080,
            ["Hydrogen sulfide"]  = 0.030,
            ["Water"]             = 0.020,
            ["Nitrogen"]          = 0.010,
            ["Ethane"]            = 0.005,
            ["Propane"]           = 0.002,
        });

        ((dynamic)feedSepLiquid).SetPressure(3_500_000.0);

        // Lean amine recycle tear-stream seed.
        // ~40 wt% MDEA in water ≈ x_amine = 0.09 mol/mol (molar basis).
        // Starting with a physically plausible amine concentration avoids
        // a near-pure-water stream that would give an almost empty absorbedComps
        // stream on the first iteration and prevent the recycle from converging.
        ((dynamic)recycleToAbs).SetTemperature(313.15);
        ((dynamic)recycleToAbs).SetPressure(3_500_000.0);
        ((dynamic)recycleToAbs).SetMassFlow(10.0);
        ApplyComposition(recycleToAbs, new Dictionary<string, double>
        {
            ["Water"]   = 0.91,
            [amineName] = 0.09,
        });

        // -----------------------------------------------------------------------
        // Dynamic setup
        // -----------------------------------------------------------------------
        sim.DynamicMode = true;

        var integ = new Integrator
        {
            ID = Guid.NewGuid().ToString(),
            Description = "Acid gas baseline integrator",
            IntegrationStep = TimeSpan.FromSeconds(1),
            Duration = TimeSpan.FromHours(1),
            CalculationRateControl = 1,
            CalculationRateEquilibrium = 5,
            CalculationRatePressureFlow = 1,
            RealTime = false,
            RealTimeStepMs = 1000
        };

        var sch = new Schedule
        {
            ID = Guid.NewGuid().ToString(),
            Description = "Baseline dynamic schedule",
            CurrentIntegrator = integ.ID,
            UseCurrentStateAsInitial = true,
            UsesEventList = false,
            UsesCauseAndEffectMatrix = false,
            ResetContentsOfAllObjects = false
        };

        ConfigureKpiMonitors(integ, feed, salesGas, sweetGas, richAmine, acidicGas, recycleToAbs, regenHeater);

        sim.DynamicsManager.IntegratorList.Add(integ.ID, integ);
        sim.DynamicsManager.ScheduleList.Add(sch.ID, sch);
        sim.DynamicsManager.CurrentSchedule = sch.ID;

        var pre = new Script
        {
            ID = Guid.NewGuid().ToString(),
            Title = "Feed Profile (Pre-Step)",
            Linked = true,
            LinkedObjectType = DWSIM.Interfaces.Enums.Scripts.ObjectType.Integrator,
            LinkedEventType = DWSIM.Interfaces.Enums.Scripts.EventType.IntegratorPreStep,
            PythonInterpreter = DWSIM.Interfaces.Enums.Scripts.Interpreter.IronPython,
            ScriptText = File.ReadAllText("integrator_pre_step_feed_profile.py")
        };

        var post = new Script
        {
            ID = Guid.NewGuid().ToString(),
            Title = "KPI Logger (Post-Step)",
            Linked = true,
            LinkedObjectType = DWSIM.Interfaces.Enums.Scripts.ObjectType.Integrator,
            LinkedEventType = DWSIM.Interfaces.Enums.Scripts.EventType.IntegratorStep,
            PythonInterpreter = DWSIM.Interfaces.Enums.Scripts.Interpreter.IronPython,
            ScriptText = File.ReadAllText("integrator_post_step_kpi_logger.py")
        };

        sim.Scripts.Add(pre.ID, pre);
        sim.Scripts.Add(post.ID, post);

        automation.SaveFlowsheet(sim, outputFile, true);
    }

    // -----------------------------------------------------------------------
    // KPI monitoring helpers
    // -----------------------------------------------------------------------

    private static void ConfigureKpiMonitors(
        Integrator integ,
        dynamic feed, dynamic salesGas, dynamic sweetGas,
        dynamic richAmine, dynamic acidicGas, dynamic leanAmine, dynamic regenHeater)
    {
        // Sales gas quality (primary process KPIs)
        TryAddMonitoredVariable(integ, salesGas,    "PROP_MS_106/Hydrogen sulfide", "Sales gas H2S mole fraction");
        TryAddMonitoredVariable(integ, salesGas,    "PROP_MS_106/Carbon dioxide",   "Sales gas CO2 mole fraction");

        // Sweet gas directly from absorber surrogate
        TryAddMonitoredVariable(integ, sweetGas,    "PROP_MS_106/Hydrogen sulfide", "Sweet gas H2S mole fraction");
        TryAddMonitoredVariable(integ, sweetGas,    "PROP_MS_106/Carbon dioxide",   "Sweet gas CO2 mole fraction");

        // Rich amine loading proxy (mole fractions as surrogate for acid gas loading)
        TryAddMonitoredVariable(integ, richAmine,   "PROP_MS_106/Hydrogen sulfide", "Rich amine H2S mole fraction");
        TryAddMonitoredVariable(integ, richAmine,   "PROP_MS_106/Carbon dioxide",   "Rich amine CO2 mole fraction");

        // Acid gas product
        TryAddMonitoredVariable(integ, acidicGas,   "PROP_MS_2",                    "Acid gas product mass flow");

        // Lean amine recycle temperature
        TryAddMonitoredVariable(integ, leanAmine,   "PROP_MS_0",                    "Lean amine temperature");

        // Regeneration heater duty
        TryAddMonitoredVariable(integ, regenHeater, "PROP_HT_0",                    "Regen heater duty");

        // Feed throughput
        TryAddMonitoredVariable(integ, feed,        "PROP_MS_2",                    "Feed mass flow");
    }

    /// <summary>
    /// Adds a monitored variable to the integrator if the property exists on the object.
    /// Missing properties are silently skipped (no throw) so that a property-ID
    /// mismatch does not abort flowsheet generation.
    /// </summary>
    private static void TryAddMonitoredVariable(
        Integrator integ, dynamic obj, string propertyId, string description)
    {
        try
        {
            string[] props = obj.GetProperties(PropertyType.ALL);
            if (!props.Contains(propertyId)) return;

            integ.MonitoredVariables.Add(new MonitoredVariable
            {
                ID           = Guid.NewGuid().ToString(),
                Description  = description,
                ObjectID     = obj.Name,
                PropertyID   = propertyId,
                PropertyUnits = obj.GetPropertyUnit(propertyId) ?? ""
            });
        }
        catch { /* property not available on this object type; skip silently */ }
    }

    // -----------------------------------------------------------------------
    // Compound helpers
    // -----------------------------------------------------------------------

    private static void AddCompoundOrThrow(IFlowsheet sim, string name)
    {
        try   { sim.AddCompound(name); }
        catch (Exception ex) { throw new Exception($"Required compound '{name}' could not be added.", ex); }
    }

    private static bool AddCompoundIfAvailable(IFlowsheet sim, string name)
    {
        try   { sim.AddCompound(name); return true; }
        catch { return false; }
    }

    private static string AddFirstAvailableCompound(IFlowsheet sim, IEnumerable<string> names)
    {
        foreach (var n in names)
            if (AddCompoundIfAvailable(sim, n)) return n;
        return null;
    }

    /// <summary>
    /// Sets mole fractions on a material stream from a name→fraction dictionary.
    /// Compounds absent from the stream are silently skipped; surviving fractions
    /// are re-normalised so the composition always sums to 1.
    /// </summary>
    private static void ApplyComposition(object stream, Dictionary<string, double> fracs)
    {
        dynamic s = stream;
        var comps = s.Phases[0].Compounds;

        double sum = 0.0;
        foreach (var kvp in fracs)
            if (comps.ContainsKey(kvp.Key)) sum += kvp.Value;

        if (sum <= 0.0) return;

        foreach (var kvp in fracs)
            if (comps.ContainsKey(kvp.Key))
                comps[kvp.Key].MoleFraction = kvp.Value / sum;
    }
}
