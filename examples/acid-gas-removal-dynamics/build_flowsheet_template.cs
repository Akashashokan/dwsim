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

// Acid gas removal dynamic template with amine-ready defaults and KPI monitoring.

public static class AcidGasRemovalDynamicTemplate
{
    public static void Generate(string outputFile)
    {
        var automation = new Automation3();
        var sim = automation.CreateFlowsheet();

        // Gas-side compounds + optional heavies.
        AddCompoundOrThrow(sim, "Methane");
        AddCompoundOrThrow(sim, "Carbon dioxide");
        AddCompoundOrThrow(sim, "Hydrogen sulfide");
        AddCompoundOrThrow(sim, "Water");
        AddCompoundIfAvailable(sim, "Nitrogen");
        AddCompoundIfAvailable(sim, "Ethane");
        AddCompoundIfAvailable(sim, "Propane");

        // Amine package compounds: pick one amine at minimum (prefer MDEA, then MEA, then DEA).
        var amineAdded = AddFirstAvailableCompound(sim, new[]
        {
            "Methyl diethanolamine",
            "Monoethanolamine",
            "Diethanolamine"
        });
        if (!amineAdded)
            throw new Exception("Could not add any amine compound (MDEA/MEA/DEA). Please verify your component database.");

        // Thermodynamic package selection for amine systems.
        var ppName = SelectAminePropertyPackage(sim);
        sim.CreateAndAddPropertyPackage(ppName);

        // --- Core process blocks ---
        var feed = sim.AddObject(ObjectType.MaterialStream, 40, 220, "Feed");
        var saturationMixer = sim.AddObject(ObjectType.Mixer, 120, 220, "Saturation mixer");
        var feedSeparator = sim.AddObject(ObjectType.Vessel, 210, 220, "Feed water separator");
        var absorber = sim.AddObject(ObjectType.AbsorptionColumn, 340, 220, "ABSORBER");
        var richCooler = sim.AddObject(ObjectType.Cooler, 500, 180, "Gas-gas HX");
        var salesSeparator = sim.AddObject(ObjectType.Vessel, 620, 170, "Sales gas water sep");

        var regenerator1 = sim.AddObject(ObjectType.DistillationColumn, 540, 300, "REGENERATOR I");
        var regenerator2 = sim.AddObject(ObjectType.DistillationColumn, 700, 300, "REGENERATOR II");
        var regenerator3 = sim.AddObject(ObjectType.DistillationColumn, 860, 300, "REGENERATOR III");

        var leanPump = sim.AddObject(ObjectType.Pump, 980, 430, "Amine rec. pump");
        var leanSaturator = sim.AddObject(ObjectType.Mixer, 1080, 430, "Rec. amine saturator");

        // Key streams
        var saturatedFeed = sim.AddObject(ObjectType.MaterialStream, 170, 220, "Saturated feed");
        var absFeed = sim.AddObject(ObjectType.MaterialStream, 310, 180, "Abs. feed");
        var hotRichGas = sim.AddObject(ObjectType.MaterialStream, 420, 180, "Hot rich gas");
        var coolRichGas = sim.AddObject(ObjectType.MaterialStream, 560, 180, "Cool rich gas");
        var salesGas = sim.AddObject(ObjectType.MaterialStream, 760, 140, "Sales gas");

        var richAmine = sim.AddObject(ObjectType.MaterialStream, 420, 255, "Rich amine");
        var iFlashOut = sim.AddObject(ObjectType.MaterialStream, 560, 360, "Amine to regen II");
        var iiFlashOut = sim.AddObject(ObjectType.MaterialStream, 720, 360, "Amine to regen III");
        var acidicGas1 = sim.AddObject(ObjectType.MaterialStream, 580, 255, "Acid gas from regen I");
        var acidicGas2 = sim.AddObject(ObjectType.MaterialStream, 740, 255, "Acid gas from regen II");
        var acidicGas = sim.AddObject(ObjectType.MaterialStream, 890, 255, "Acidic gas to compressor");

        var leanAmine = sim.AddObject(ObjectType.MaterialStream, 930, 470, "LEAN AMINE");
        var pumpedLeanAmine = sim.AddObject(ObjectType.MaterialStream, 1030, 430, "Pumped lean amine");
        var leanToAbs = sim.AddObject(ObjectType.MaterialStream, 1160, 430, "Lean amine to recycle");
        var amineRecycle = sim.AddObject(ObjectType.OT_Recycle, 1260, 420, "Amine recycle");
        var recycleToAbs = sim.AddObject(ObjectType.MaterialStream, 300, 300, "Input lean amine");

        // Liquid bottoms of the two separator vessels (required — Vessel.Calculate throws if
        // OutputConnectors(1) is unattached regardless of the liquid flow being zero).
        var feedSepLiquid = sim.AddObject(ObjectType.MaterialStream, 210, 290, "Feed sep. water");
        var salesSepLiquid = sim.AddObject(ObjectType.MaterialStream, 700, 220, "Sales gas condensate");

        foreach (var o in sim.SimulationObjects.Values)
            ((dynamic)o.GraphicObject).PositionConnectors();

        // Non-column connections (simple streams through Mixer/Vessel/Cooler/Pump):
        // These only need the graphical link — no internal stream registry involved.
        sim.ConnectObjects(feed.GraphicObject, saturationMixer.GraphicObject, 0, 0);
        sim.ConnectObjects(saturationMixer.GraphicObject, saturatedFeed.GraphicObject, 0, 0);
        sim.ConnectObjects(saturatedFeed.GraphicObject, feedSeparator.GraphicObject, 0, 0);
        sim.ConnectObjects(feedSeparator.GraphicObject, absFeed.GraphicObject, 0, 0);
        // Vessel output connector 1 (liquid bottom) must be attached or Vessel.Calculate throws.
        sim.ConnectObjects(feedSeparator.GraphicObject, feedSepLiquid.GraphicObject, 1, 0);
        sim.ConnectObjects(hotRichGas.GraphicObject, richCooler.GraphicObject, 0, 0);
        sim.ConnectObjects(richCooler.GraphicObject, coolRichGas.GraphicObject, 0, 0);
        sim.ConnectObjects(coolRichGas.GraphicObject, salesSeparator.GraphicObject, 0, 0);
        sim.ConnectObjects(salesSeparator.GraphicObject, salesGas.GraphicObject, 0, 0);
        // Vessel output connector 1 (liquid bottom) must be attached or Vessel.Calculate throws.
        sim.ConnectObjects(salesSeparator.GraphicObject, salesSepLiquid.GraphicObject, 1, 0);
        sim.ConnectObjects(leanAmine.GraphicObject, leanPump.GraphicObject, 0, 0);
        sim.ConnectObjects(leanPump.GraphicObject, pumpedLeanAmine.GraphicObject, 0, 0);
        sim.ConnectObjects(pumpedLeanAmine.GraphicObject, leanSaturator.GraphicObject, 0, 0);
        sim.ConnectObjects(leanSaturator.GraphicObject, leanToAbs.GraphicObject, 0, 0);
        // Recycle block breaks the lean-amine cycle so the solver can determine
        // a finite calculation order without hitting the "Infinite loop detected" error.
        sim.ConnectObjects(leanToAbs.GraphicObject, amineRecycle.GraphicObject, 0, 0);
        sim.ConnectObjects(amineRecycle.GraphicObject, recycleToAbs.GraphicObject, 0, 0);

        // -----------------------------------------------------------------------
        // Column connections: MUST use each column's own Connect* API methods.
        //
        // Raw sim.ConnectObjects() only creates the graphical link; it does NOT
        // populate the column's internal MaterialStreams dictionary.  That
        // dictionary is the sole source of feed/product data for the column
        // solver (GetSolverInputData builds F[], FT[], HF[] arrays exclusively
        // from MaterialStreams entries).
        //
        // Consequences of using raw ConnectObjects() instead:
        //   • Validate() throws "DCConnectionMissingException" (feedok / cmok /
        //     rmok flags all stay false because no entries exist in
        //     MaterialStreams).
        //   • AbsorptionColumn solver throws "The absorber needs a feed stream
        //     connected to the first/last stage" (FT.First / FT.Last = 0 because
        //     no Feed entries were found in MaterialStreams).
        // -----------------------------------------------------------------------

        // --- Absorber ---
        // Gas (absFeed) enters at the BOTTOM stage; lean amine (recycleToAbs)
        // enters at the TOP stage (stage 0).  Treated gas exits at the top
        // (ConnectDistillate → Behavior.Distillate), rich liquid exits at the
        // bottom (ConnectBottoms → Behavior.BottomsLiquid).
        int absNs = ((dynamic)absorber).Stages.Count - 1; // index of last (bottom) stage
        ((dynamic)absorber).ConnectFeed(absFeed, absNs);      // gas at bottom stage
        ((dynamic)absorber).ConnectFeed(recycleToAbs, 0);     // lean amine at top stage
        ((dynamic)absorber).ConnectDistillate(hotRichGas);    // treated gas exits top
        ((dynamic)absorber).ConnectBottoms(richAmine);        // rich amine exits bottom

        // --- Three-stage regeneration cascade ---
        // Physical process: liquid partially-stripped amine flows DOWN the cascade
        // (regen1 → regen2 → regen3) as bottoms products.  Acid gas vapour is
        // released overhead at EACH stage.
        //
        //   richAmine → [regen1] → acidicGas1 (overhead)
        //                        → iFlashOut (bottoms, amine to regen2)
        //   iFlashOut → [regen2] → acidicGas2 (overhead)
        //                        → iiFlashOut (bottoms, amine to regen3)
        //   iiFlashOut→ [regen3] → acidicGas  (overhead, final acid-gas product)
        //                        → leanAmine  (bottoms, lean amine to pump)
        ((dynamic)regenerator1).ConnectFeed(richAmine, 1);      // feed just below condenser
        ((dynamic)regenerator1).ConnectDistillate(acidicGas1);  // acid gas overhead
        ((dynamic)regenerator1).ConnectBottoms(iFlashOut);      // partially-stripped amine → regen2

        ((dynamic)regenerator2).ConnectFeed(iFlashOut, 1);      // feed just below condenser
        ((dynamic)regenerator2).ConnectDistillate(acidicGas2);  // acid gas overhead
        ((dynamic)regenerator2).ConnectBottoms(iiFlashOut);     // further-stripped amine → regen3

        ((dynamic)regenerator3).ConnectFeed(iiFlashOut, 1);     // feed just below condenser
        ((dynamic)regenerator3).ConnectDistillate(acidicGas);   // final acid gas product
        ((dynamic)regenerator3).ConnectBottoms(leanAmine);      // lean amine → pump

        // -----------------------------------------------------------------------
        // Cooler: switch from default HeatRemoved (SpecType=PH on outlet) to
        // OutletTemperature (SpecType=TP on outlet).
        //
        // Root cause of "PH Flash [Electrolyte]: Temperature did not converge":
        //   After the absorber computes a valid gas outlet (hotRichGas), the
        //   Cooler in default HeatRemoved/DeltaQ=0 mode copies H_in to H_out and
        //   marks coolRichGas with SpecType = Pressure_and_Enthalpy plus
        //   AtEquilibrium = False.  The FlowsheetSolver then calls
        //   coolRichGas.Calculate(), which invokes ElectrolyteSVLE.Flash_PH.
        //   For gas-phase acid-gas mixtures the Newton loop inside Flash_PH
        //   diverges within 25 iterations and throws the exception.
        //
        // Fix: OutletTemperature mode uses a PT flash internally and sets
        //   coolRichGas.SpecType = Temperature_and_Pressure.  The solver then
        //   recalculates coolRichGas with the robust Flash_PT path instead.
        // -----------------------------------------------------------------------
        ((dynamic)richCooler).CalcMode = 1;            // 1 = OutletTemperature
        ((dynamic)richCooler).OutletTemperature = 305.15; // cool ~8 K to 32 °C

        // -----------------------------------------------------------------------
        // Regenerator column specs.
        //
        // DistillationColumn.Specs["C"] and ["R"] default to:
        //   C: Stream_Ratio (reflux ratio) = me.RefluxRatio = 5.0
        //   R: Product_Molar_Flow_Rate     = me.DistillateFlowRate = 0 mol/s
        //
        // A reboiler spec of 0 mol/s bottoms means all feed goes overhead, which
        // collapses the material balance and causes the column to diverge.
        // We override both specs with a reflux ratio (condenser) and boilup ratio
        // (reboiler) = 1.0 each — a simple but numerically stable starting point
        // for a stripping-oriented regeneration column.
        // -----------------------------------------------------------------------
        ((dynamic)regenerator1).SetCondenserSpec("Reflux Ratio", 1.0, "");
        ((dynamic)regenerator1).SetReboilerSpec("Boilup Ratio", 1.0, "");
        ((dynamic)regenerator2).SetCondenserSpec("Reflux Ratio", 1.0, "");
        ((dynamic)regenerator2).SetReboilerSpec("Boilup Ratio", 1.0, "");
        ((dynamic)regenerator3).SetCondenserSpec("Reflux Ratio", 1.0, "");
        ((dynamic)regenerator3).SetReboilerSpec("Boilup Ratio", 1.0, "");

        // -----------------------------------------------------------------------
        // Amine recirculation pump: set a pressure rise to match absorber inlet.
        //
        // The pump CalcMode defaults to Delta_P = 0, which leaves the outlet at
        // the same pressure as the regenerator (~101 325 Pa = 1 atm).  The absorber
        // feed gas is at ~3.5 MPa; without pump pressure the lean amine would
        // enter the absorber at a far lower pressure than the gas, making the
        // column material balance physically inconsistent.
        //
        // DeltaP = 3 400 000 Pa brings the lean amine from ~1 atm up to ~35 bar,
        // matching the absorber operating pressure.
        // -----------------------------------------------------------------------
        ((dynamic)leanPump).DeltaP = 3_400_000.0; // Pa (~3.4 MPa pump head)

        // Baseline feed specs (SI). Equivalent of P/T/flow sanity check.
        ((dynamic)feed).SetTemperature(313.15);
        ((dynamic)feed).SetPressure(3_500_000.0);
        ((dynamic)feed).SetMassFlow(2.0);

        // Set wellhead gas feed composition.  Without a nonzero composition the
        // electrolyte PH-flash Newton solver (ElectrolyteSVLE.Flash_PH) sees a
        // degenerate enthalpy objective H(T) = 0 at every T and throws
        // "Temperature did not converge" before any unit operation can run.
        ApplyComposition(feed, new Dictionary<string, double>
        {
            ["Methane"]               = 0.850,
            ["Carbon dioxide"]        = 0.080,
            ["Hydrogen sulfide"]      = 0.030,
            ["Water"]                 = 0.020,
            ["Nitrogen"]              = 0.010,
            ["Ethane"]                = 0.005,
            ["Propane"]               = 0.002,
            // Amine compounds are zero in the raw gas feed; they enter only via
            // the lean-amine recycle loop.
            ["Methyl diethanolamine"] = 0.000,
            ["Monoethanolamine"]      = 0.000,
            ["Diethanolamine"]        = 0.000,
        });

        // Seed the lean-amine recycle tear stream with a physically meaningful
        // initial guess so the absorber solver has a valid starting point on the
        // very first iteration (before the Recycle block has converged).
        ((dynamic)recycleToAbs).SetTemperature(313.15);
        ((dynamic)recycleToAbs).SetPressure(3_500_000.0);
        ((dynamic)recycleToAbs).SetMassFlow(10.0);
        ApplyComposition(recycleToAbs, new Dictionary<string, double>
        {
            ["Water"]                 = 0.700,
            // Only the amine that was actually added to the flowsheet will match.
            ["Methyl diethanolamine"] = 0.290,
            ["Monoethanolamine"]      = 0.290,
            ["Diethanolamine"]        = 0.290,
            ["Carbon dioxide"]        = 0.005,
            ["Hydrogen sulfide"]      = 0.003,
            ["Methane"]               = 0.001,
        });

        // Dynamic setup: one integrator + one schedule.
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

        // Preconfigure monitored variables for requested KPIs.
        ConfigureKpiMonitors(sim, integ,
            feed, salesGas, absorber, hotRichGas, richAmine, acidicGas, leanAmine, regenerator3);

        sim.DynamicsManager.IntegratorList.Add(integ.ID, integ);
        sim.DynamicsManager.ScheduleList.Add(sch.ID, sch);
        sim.DynamicsManager.CurrentSchedule = sch.ID;

        // Attach script manager scripts (pre-step and post-step).
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

    private static void ConfigureKpiMonitors(
        IFlowsheet sim, Integrator integ,
        dynamic feed, dynamic salesGas, dynamic absorber, dynamic absorberTopGas, dynamic absorberBottomLiquid,
        dynamic acidGas, dynamic leanAmine, dynamic regenerator)
    {
        // Sales gas H2S/CO2
        AddMonitoredVariable(integ, salesGas, "PROP_MS_106/Hydrogen sulfide", "Sales gas H2S mole fraction");
        AddMonitoredVariable(integ, salesGas, "PROP_MS_106/Carbon dioxide", "Sales gas CO2 mole fraction");

        // Absorber ΔP (represented by top and bottom pressures for direct difference).
        AddMonitoredVariable(integ, absorber, "PROP_AC_0", "Absorber top pressure");
        AddMonitoredVariable(integ, absorber, "PROP_AC_1", "Absorber bottom pressure");

        // Absorber top/bottom compositions.
        AddMonitoredVariable(integ, absorberTopGas, "PROP_MS_106/Hydrogen sulfide", "Absorber top gas H2S mole fraction");
        AddMonitoredVariable(integ, absorberTopGas, "PROP_MS_106/Carbon dioxide", "Absorber top gas CO2 mole fraction");
        AddMonitoredVariable(integ, absorberBottomLiquid, "PROP_MS_102/Hydrogen sulfide", "Absorber bottom liquid H2S mole fraction");
        AddMonitoredVariable(integ, absorberBottomLiquid, "PROP_MS_102/Carbon dioxide", "Absorber bottom liquid CO2 mole fraction");

        // Regenerator overhead acid gas flow.
        AddMonitoredVariable(integ, acidGas, "PROP_MS_2", "Regenerator overhead acid gas mass flow");

        // Lean amine loading / temperature.
        AddMonitoredVariable(integ, leanAmine, "CO2 Loading", "Lean amine CO2 loading");
        AddMonitoredVariable(integ, leanAmine, "PROP_MS_0", "Lean amine temperature");

        // Reboiler duty (using third regenerator block in this template).
        AddMonitoredVariable(integ, regenerator, "PROP_DC_6", "Regenerator III reboiler duty");

        // Keep feed flow monitored as operation sanity signal.
        AddMonitoredVariable(integ, feed, "PROP_MS_2", "Feed mass flow");
    }

    private static void AddMonitoredVariable(Integrator integ, dynamic obj, string propertyId, string description)
    {
        string[] props = obj.GetProperties(PropertyType.ALL);
        if (!props.Contains(propertyId))
        {
            throw new Exception($"Required KPI property '{propertyId}' not available on object '{obj.GraphicObject.Tag}'.");
        }

        var mv = new MonitoredVariable
        {
            ID = Guid.NewGuid().ToString(),
            Description = description,
            ObjectID = obj.Name,
            PropertyID = propertyId,
            PropertyUnits = obj.GetPropertyUnit(propertyId) ?? ""
        };

        integ.MonitoredVariables.Add(mv);
    }

    private static string SelectAminePropertyPackage(IFlowsheet sim)
    {
        var pps = sim.GetAvailablePropertyPackages().ToList();

        string Match(params string[] terms)
        {
            return pps.FirstOrDefault(pp =>
            {
                var l = pp.ToLowerInvariant();
                return terms.All(t => l.Contains(t));
            });
        }

        // Prefer amine-specific package, then electrolyte methods, then fallback.
        var preferred =
            Match("amines") ??
            Match("electrolyte", "nrtl") ??
            Match("electrolyte") ??
            Match("peng", "robinson") ??
            pps.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(preferred))
            throw new Exception("No property package is available in this DWSIM installation.");

        return preferred;
    }

    private static void AddCompoundOrThrow(IFlowsheet sim, string name)
    {
        try
        {
            sim.AddCompound(name);
        }
        catch (Exception ex)
        {
            throw new Exception($"Required compound '{name}' could not be added.", ex);
        }
    }

    private static bool AddCompoundIfAvailable(IFlowsheet sim, string name)
    {
        try
        {
            sim.AddCompound(name);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool AddFirstAvailableCompound(IFlowsheet sim, IEnumerable<string> names)
    {
        foreach (var n in names)
        {
            if (AddCompoundIfAvailable(sim, n)) return true;
        }
        return false;
    }

    /// <summary>
    /// Sets mole fractions on a material stream from a name→fraction dictionary.
    /// Compounds absent from the stream are silently skipped; the surviving fractions
    /// are re-normalised to 1 so the composition is always valid.
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
