using System;
using System.Collections.Generic;
using System.Linq;
using DWSIM.Automation;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;
using DWSIM.Interfaces.Enums.GraphicObjects;
using DWSIM.Thermodynamics.PropertyPackages;
using DWSIM.UnitOperations.Streams;
using DWSIM.UnitOperations.UnitOperations;
using DWSIM.UnitOperations.UnitOperations.Auxiliary;

public static class NaturalGasSimpleFlowsheet
{
    public static void Generate(string outputFile)
    {
        var automation = new Automation3();
        var sim = automation.CreateFlowsheet();
        sim.Options.SimulationName = "Natural Gas Separation - Simple to Deethanizer";

        foreach (var compound in new[]
        {
            "Methane",
            "Ethane",
            "Propane",
            "Isobutane",
            "n-Butane",
            "Isopentane",
            "n-Pentane",
            "n-Hexane",
            "n-Heptane",
            "Nitrogen"
        })
        {
            AddCompoundOrThrow(sim, compound);
        }

        var ppName = sim.GetAvailablePropertyPackages()
            .FirstOrDefault(x =>
            {
                var name = x.ToLowerInvariant();
                return name.Contains("peng") && name.Contains("robinson");
            })
            ?? sim.GetAvailablePropertyPackages().FirstOrDefault();
        if (ppName == null) throw new Exception("No property package is available.");

        var pp = sim.CreateAndAddPropertyPackage(ppName);
        var ppc = (PropertyPackage)(object)pp;
        sim.Options.PropertyPackage = pp;

        var feed = sim.AddObject(ObjectType.MaterialStream, 50, 120, "NG-Feed");
        var s02 = sim.AddObject(ObjectType.MaterialStream, 150, 120, "S-02");
        var s03 = sim.AddObject(ObjectType.MaterialStream, 250, 60, "S-03");
        var s04 = sim.AddObject(ObjectType.MaterialStream, 250, 150, "S-04");
        var s05 = sim.AddObject(ObjectType.MaterialStream, 350, 150, "S-05");
        var s06 = sim.AddObject(ObjectType.MaterialStream, 450, 150, "S-06");
        var s07 = sim.AddObject(ObjectType.MaterialStream, 450, 90, "S-07");
        var s08 = sim.AddObject(ObjectType.MaterialStream, 600, 90, "S-08");
        var s10 = sim.AddObject(ObjectType.MaterialStream, 700, 10, "S-10");
        var s11 = sim.AddObject(ObjectType.MaterialStream, 750, 90, "S-11");
        var s12 = sim.AddObject(ObjectType.MaterialStream, 900, 90, "S-12");
        var s13 = sim.AddObject(ObjectType.MaterialStream, 900, 180, "S-13");
        var s14 = sim.AddObject(ObjectType.MaterialStream, 1020, 120, "S-14");
        var s15 = sim.AddObject(ObjectType.MaterialStream, 1020, 60, "S-15");
        var s16 = sim.AddObject(ObjectType.MaterialStream, 1180, 30, "S-16");
        var s17 = sim.AddObject(ObjectType.MaterialStream, 1180, 70, "S-17");
        var s19 = sim.AddObject(ObjectType.MaterialStream, 1180, 140, "S-19");
        var s20 = sim.AddObject(ObjectType.MaterialStream, 1330, 80, "S-20");
        var s21 = sim.AddObject(ObjectType.MaterialStream, 1330, 140, "S-21");
        var s22 = sim.AddObject(ObjectType.MaterialStream, 1330, 190, "S-22");
        var s23 = sim.AddObject(ObjectType.MaterialStream, 1100, 210, "S-23");
        var s24 = sim.AddObject(ObjectType.MaterialStream, 1520, 120, "S-24");
        var s25 = sim.AddObject(ObjectType.MaterialStream, 1520, 190, "S-25");
        var methane = sim.AddObject(ObjectType.MaterialStream, 1700, 100, "Methane");
        var ethane = sim.AddObject(ObjectType.MaterialStream, 1850, 160, "Ethane");
        var deethBtm = sim.AddObject(ObjectType.MaterialStream, 1850, 240, "Deeth_Btm");

        var e1 = sim.AddObject(ObjectType.EnergyStream, 120, 180, "E-1");
        var e02 = sim.AddObject(ObjectType.EnergyStream, 330, 220, "E-02");
        var e03 = sim.AddObject(ObjectType.EnergyStream, 430, 220, "E-03");
        var e04 = sim.AddObject(ObjectType.EnergyStream, 720, 220, "E-04");
        var e05 = sim.AddObject(ObjectType.EnergyStream, 860, 240, "E-05");
        var e08 = sim.AddObject(ObjectType.EnergyStream, 1280, 240, "E-08");

        var com01 = sim.AddObject(ObjectType.Compressor, 100, 120, "COM-01");
        var split01 = sim.AddObject(ObjectType.Splitter, 200, 120, "SPLIT-01");
        var cool01 = sim.AddObject(ObjectType.Cooler, 300, 150, "COOL-01");
        var cool02 = sim.AddObject(ObjectType.Cooler, 400, 150, "COOL-02");
        var mix01 = sim.AddObject(ObjectType.Mixer, 500, 90, "MIX-01");
        var hex01 = sim.AddObject(ObjectType.HeatExchanger, 650, 90, "HEX-01");
        var cool03 = sim.AddObject(ObjectType.Cooler, 720, 90, "COOL-03");
        var flash01 = sim.AddObject(ObjectType.Vessel, 840, 100, "FLASH-01");
        var split02 = sim.AddObject(ObjectType.Splitter, 970, 90, "SPLIT-02");
        var hex02 = sim.AddObject(ObjectType.HeatExchanger, 1120, 60, "HEX-02");
        var exp01 = sim.AddObject(ObjectType.Expander, 1120, 130, "EXP-01");
        var flash02 = sim.AddObject(ObjectType.Vessel, 1260, 140, "FLASH-02");
        var valv01 = sim.AddObject(ObjectType.Valve, 1030, 210, "VALV-01");
        var valv02 = sim.AddObject(ObjectType.Valve, 1260, 80, "VALV-02");
        var demeth = sim.AddObject(ObjectType.ComponentSeparator, 1450, 150, "DEMETH");
        var deeth = sim.AddObject(ObjectType.ComponentSeparator, 1780, 200, "DEETH");

        foreach (var obj in sim.SimulationObjects.Values)
            ((dynamic)obj.GraphicObject).PositionConnectors();

        sim.ConnectObjects(feed.GraphicObject, com01.GraphicObject, 0, 0);
        sim.ConnectObjects(e1.GraphicObject, com01.GraphicObject, 0, 1);
        sim.ConnectObjects(com01.GraphicObject, s02.GraphicObject, 0, 0);
        sim.ConnectObjects(s02.GraphicObject, split01.GraphicObject, 0, 0);
        sim.ConnectObjects(split01.GraphicObject, s03.GraphicObject, 0, 0);
        sim.ConnectObjects(split01.GraphicObject, s04.GraphicObject, 1, 0);
        sim.ConnectObjects(s03.GraphicObject, mix01.GraphicObject, 0, 0);
        sim.ConnectObjects(s04.GraphicObject, cool01.GraphicObject, 0, 0);
        sim.ConnectObjects(e02.GraphicObject, cool01.GraphicObject, 0, 1);
        sim.ConnectObjects(cool01.GraphicObject, s05.GraphicObject, 0, 0);
        sim.ConnectObjects(s05.GraphicObject, cool02.GraphicObject, 0, 0);
        sim.ConnectObjects(e03.GraphicObject, cool02.GraphicObject, 0, 1);
        sim.ConnectObjects(cool02.GraphicObject, s06.GraphicObject, 0, 0);
        sim.ConnectObjects(s06.GraphicObject, mix01.GraphicObject, 0, 1);
        sim.ConnectObjects(mix01.GraphicObject, s07.GraphicObject, 0, 0);
        sim.ConnectObjects(s07.GraphicObject, hex01.GraphicObject, 0, 0);
        sim.ConnectObjects(hex01.GraphicObject, s08.GraphicObject, 0, 0);
        sim.ConnectObjects(hex01.GraphicObject, s10.GraphicObject, 1, 0);
        sim.ConnectObjects(s08.GraphicObject, cool03.GraphicObject, 0, 0);
        sim.ConnectObjects(e04.GraphicObject, cool03.GraphicObject, 0, 1);
        sim.ConnectObjects(cool03.GraphicObject, s11.GraphicObject, 0, 0);
        sim.ConnectObjects(s11.GraphicObject, flash01.GraphicObject, 0, 0);
        sim.ConnectObjects(e05.GraphicObject, flash01.GraphicObject, 0, 1);
        sim.ConnectObjects(flash01.GraphicObject, s12.GraphicObject, 0, 0);
        sim.ConnectObjects(flash01.GraphicObject, s13.GraphicObject, 1, 0);
        sim.ConnectObjects(s12.GraphicObject, split02.GraphicObject, 0, 0);
        sim.ConnectObjects(split02.GraphicObject, s15.GraphicObject, 0, 0);
        sim.ConnectObjects(split02.GraphicObject, s14.GraphicObject, 1, 0);
        sim.ConnectObjects(s15.GraphicObject, hex02.GraphicObject, 0, 0);
        sim.ConnectObjects(s14.GraphicObject, exp01.GraphicObject, 0, 0);
        sim.ConnectObjects(exp01.GraphicObject, s19.GraphicObject, 0, 0);
        sim.ConnectObjects(s19.GraphicObject, flash02.GraphicObject, 0, 0);
        sim.ConnectObjects(e08.GraphicObject, flash02.GraphicObject, 0, 1);
        sim.ConnectObjects(flash02.GraphicObject, s21.GraphicObject, 0, 0);
        sim.ConnectObjects(flash02.GraphicObject, s22.GraphicObject, 1, 0);
        sim.ConnectObjects(s13.GraphicObject, valv01.GraphicObject, 0, 0);
        sim.ConnectObjects(valv01.GraphicObject, s23.GraphicObject, 0, 0);
        sim.ConnectObjects(hex02.GraphicObject, s17.GraphicObject, 0, 0);
        sim.ConnectObjects(hex02.GraphicObject, s16.GraphicObject, 1, 0);
        sim.ConnectObjects(s17.GraphicObject, valv02.GraphicObject, 0, 0);
        sim.ConnectObjects(valv02.GraphicObject, s20.GraphicObject, 0, 0);
        sim.ConnectObjects(s20.GraphicObject, demeth.GraphicObject, 0, 0);
        sim.ConnectObjects(s21.GraphicObject, demeth.GraphicObject, 0, 1);
        sim.ConnectObjects(s22.GraphicObject, demeth.GraphicObject, 0, 2);
        sim.ConnectObjects(s23.GraphicObject, demeth.GraphicObject, 0, 3);
        sim.ConnectObjects(demeth.GraphicObject, s24.GraphicObject, 0, 0);
        sim.ConnectObjects(demeth.GraphicObject, s25.GraphicObject, 1, 0);
        sim.ConnectObjects(s24.GraphicObject, methane.GraphicObject, 0, 0);
        sim.ConnectObjects(s25.GraphicObject, deeth.GraphicObject, 0, 0);
        sim.ConnectObjects(deeth.GraphicObject, ethane.GraphicObject, 0, 0);
        sim.ConnectObjects(deeth.GraphicObject, deethBtm.GraphicObject, 1, 0);

        foreach (var obj in new ISimulationObject[]
        {
            feed, s02, s03, s04, s05, s06, s07, s08, s10, s11, s12, s13, s14, s15, s16, s17, s19, s20, s21, s22, s23, s24, s25, methane, ethane, deethBtm,
            com01, split01, cool01, cool02, mix01, hex01, cool03, flash01, split02, hex02, exp01, flash02, valv01, valv02, demeth, deeth
        })
        {
            try { ((dynamic)obj).PropertyPackage = ppc; } catch { }
        }

        var feedStream = (MaterialStream)(object)feed;
        feedStream.SetTemperature(310.0);
        feedStream.SetPressure(59.7818 * 100000.0);
        feedStream.SetMolarFlow(1464.0 / 3600.0);
        ApplyComposition(feedStream, new Dictionary<string, double>
        {
            ["Methane"] = 0.8640,
            ["Ethane"] = 0.0647,
            ["Propane"] = 0.0287,
            ["Isobutane"] = 0.0072,
            ["n-Butane"] = 0.0082,
            ["Isopentane"] = 0.0041,
            ["n-Pentane"] = 0.0031,
            ["n-Hexane"] = 0.0031,
            ["n-Heptane"] = 0.0015,
            ["Nitrogen"] = 0.0154
        });

        var com1 = (Compressor)(object)com01;
        com1.CalcMode = Compressor.CalculationMode.Delta_P;
        com1.DeltaP = 202650.0;
        com1.Efficiency = 0.75;

        var split1 = (Splitter)(object)split01;
        split1.SplitRatios[0] = 15.83 / 1464.0;
        split1.SplitRatios[1] = 1448.0 / 1464.0;

        var cooler1 = (Cooler)(object)cool01;
        cooler1.CalcMode = Cooler.CalculationMode.OutletTemperature;
        cooler1.OutletTemperature = 305.3;
        cooler1.DeltaP = 20000.0;

        var cooler2 = (Cooler)(object)cool02;
        cooler2.CalcMode = Cooler.CalculationMode.OutletTemperature;
        cooler2.OutletTemperature = 289.7;
        cooler2.DeltaP = 20000.0;

        var exchanger1 = (HeatExchanger)(object)hex01;
        exchanger1.OverallCoefficient = 1000.0;
        exchanger1.Area = 239.2836;

        var cooler3 = (Cooler)(object)cool03;
        cooler3.CalcMode = Cooler.CalculationMode.OutletTemperature;
        cooler3.OutletTemperature = 251.4;
        cooler3.DeltaP = 0.0;

        var vessel1 = (Vessel)(object)flash01;
        vessel1.PressureCalculation = PressureBehavior.Minimum;

        var splitSecondary = (Splitter)(object)split02;
        splitSecondary.SplitRatios[0] = 483.3 / 1381.0;
        splitSecondary.SplitRatios[1] = 898.0 / 1381.0;

        var exchanger2 = (HeatExchanger)(object)hex02;
        exchanger2.OverallCoefficient = 1000.0;
        exchanger2.Area = 804.7164;

        var expander = (Expander)(object)exp01;
        expander.CalcMode = Expander.CalculationMode.Delta_P;
        expander.DeltaP = 3577572.5;
        expander.Efficiency = 0.75;

        var vessel2 = (Vessel)(object)flash02;
        vessel2.PressureCalculation = PressureBehavior.Minimum;

        var valve1 = (Valve)(object)valv01;
        valve1.OutletPressure = 26.4133 * 100000.0;

        var valve2 = (Valve)(object)valv02;
        valve2.OutletPressure = 27.0083 * 100000.0;

        ConfigureSeparator((ComponentSeparator)(object)demeth, new Dictionary<string, double>
        {
            ["Methane"] = 99.5,
            ["Nitrogen"] = 99.5,
            ["Ethane"] = 8.0,
            ["Propane"] = 1.0,
            ["Isobutane"] = 0.1,
            ["n-Butane"] = 0.1,
            ["Isopentane"] = 0.01,
            ["n-Pentane"] = 0.01,
            ["n-Hexane"] = 0.01,
            ["n-Heptane"] = 0.01
        });

        ConfigureSeparator((ComponentSeparator)(object)deeth, new Dictionary<string, double>
        {
            ["Methane"] = 99.0,
            ["Ethane"] = 96.0,
            ["Propane"] = 5.0,
            ["Isobutane"] = 0.1,
            ["n-Butane"] = 0.1,
            ["Isopentane"] = 0.01,
            ["n-Pentane"] = 0.01,
            ["n-Hexane"] = 0.01,
            ["n-Heptane"] = 0.01,
            ["Nitrogen"] = 95.0
        });

        sim.RequestCalculation();
        automation.SaveFlowsheet(sim, outputFile, true);
    }

    private static void ConfigureSeparator(ComponentSeparator separator, IDictionary<string, double> componentRecoveries)
    {
        separator.SpecifiedStreamIndex = 0;
        foreach (var kvp in componentRecoveries)
        {
            separator.ComponentSepSpecs[kvp.Key] = new ComponentSeparationSpec(
                kvp.Key,
                SeparationSpec.PercentInletMolarFlow,
                kvp.Value,
                "");
        }
    }

    private static void AddCompoundOrThrow(IFlowsheet sim, string name)
    {
        try { sim.AddCompound(name); }
        catch (Exception ex) { throw new Exception($"Required compound '{name}' could not be added.", ex); }
    }

    private static void ApplyComposition(object stream, Dictionary<string, double> fractions)
    {
        dynamic s = stream;
        var compounds = s.Phases[0].Compounds;

        double total = 0.0;
        foreach (var kvp in fractions)
            if (compounds.ContainsKey(kvp.Key)) total += kvp.Value;

        if (total <= 0.0) return;

        foreach (var kvp in fractions)
            if (compounds.ContainsKey(kvp.Key))
                compounds[kvp.Key].MoleFraction = kvp.Value / total;
    }
}
