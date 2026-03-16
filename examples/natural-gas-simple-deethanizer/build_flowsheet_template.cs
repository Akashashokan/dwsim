using System;
using DWSIM.Automation;
using DWSIM.Interfaces;
using DWSIM.Interfaces.Enums;
using DWSIM.Interfaces.Enums.GraphicObjects;
using DWSIM.UnitOperations.Streams;
using DWSIM.UnitOperations.UnitOperations;

public static class NaturalGasSimpleFlowsheet
{
    public static void Generate(string outputFile)
    {
        var sim = new Automation3();
        var fs = sim.CreateFlowsheet();

        fs.Options.SimulationName = "Natural Gas Separation - Simple to Deethanizer";

        string[] comps =
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
        };

        foreach (var c in comps) fs.AddCompound(c);

        var pp = fs.CreateAndAddPropertyPackage("Peng-Robinson");
        fs.Options.PropertyPackage = pp;

        Func<string, double, double, IFlowsheetObject> addMatStream = (tag, x, y) =>
            fs.AddObject(ObjectType.MaterialStream, x, y, tag);

        Func<string, double, double, IFlowsheetObject> addEnergyStream = (tag, x, y) =>
            fs.AddObject(ObjectType.EnergyStream, x, y, tag);

        Func<ObjectType, string, double, double, IFlowsheetObject> addUnit = (type, tag, x, y) =>
            fs.AddObject(type, x, y, tag);

        Action<string, string> connect = (fromTag, toTag) =>
        {
            fs.ConnectObjects(
                fs.GetFlowsheetSimulationObject(fromTag).GraphicObject,
                fs.GetFlowsheetSimulationObject(toTag).GraphicObject
            );
        };

        Action<string, string, int, int> connectPort = (fromTag, toTag, fromPort, toPort) =>
        {
            fs.ConnectObjects(
                fs.GetFlowsheetSimulationObject(fromTag).GraphicObject,
                fs.GetFlowsheetSimulationObject(toTag).GraphicObject,
                fromPort,
                toPort
            );
        };

        addMatStream("NG-Feed", 50, 120);

        addMatStream("S-02", 150, 120);
        addMatStream("S-03", 250, 60);
        addMatStream("S-04", 250, 150);
        addMatStream("S-05", 350, 150);
        addMatStream("S-06", 450, 150);
        addMatStream("S-07", 450, 90);
        addMatStream("S-08", 600, 90);
        addMatStream("S-09", 700, 40);
        addMatStream("S-10", 700, 10);
        addMatStream("S-11", 750, 90);
        addMatStream("S-12", 900, 90);
        addMatStream("S-13", 900, 180);
        addMatStream("S-14", 1020, 120);
        addMatStream("S-15", 1020, 60);
        addMatStream("S-16", 1180, 30);
        addMatStream("S-17", 1180, 70);
        addMatStream("S-18", 1330, 30);
        addMatStream("S-19", 1180, 140);
        addMatStream("S-20", 1330, 80);
        addMatStream("S-21", 1330, 140);
        addMatStream("S-22", 1330, 190);
        addMatStream("S-23", 1100, 210);
        addMatStream("S-24", 1520, 120);
        addMatStream("S-25", 1520, 190);
        addMatStream("Methane", 1700, 100);
        addMatStream("Ethane", 1850, 160);
        addMatStream("Deeth_Btm", 1850, 240);

        addEnergyStream("E-1", 120, 180);
        addEnergyStream("E-02", 330, 220);
        addEnergyStream("E-03", 430, 220);
        addEnergyStream("E-04", 720, 220);
        addEnergyStream("E-05", 860, 240);
        addEnergyStream("E-07", 1160, 220);
        addEnergyStream("E-08", 1280, 240);

        addUnit(ObjectType.Compressor, "COM-01", 100, 120);
        addUnit(ObjectType.Splitter, "SPLIT-01", 200, 120);
        addUnit(ObjectType.Cooler, "COOL-01", 300, 150);
        addUnit(ObjectType.Cooler, "COOL-02", 400, 150);
        addUnit(ObjectType.Mixer, "MIX-01", 500, 90);
        addUnit(ObjectType.HeatExchanger, "HEX-01", 650, 90);
        addUnit(ObjectType.Cooler, "COOL-03", 720, 90);
        addUnit(ObjectType.Vessel, "FLASH-01", 840, 100);
        addUnit(ObjectType.Splitter, "SPLIT-02", 970, 90);
        addUnit(ObjectType.HeatExchanger, "HEX-02", 1120, 60);
        addUnit(ObjectType.Expander, "EXP-01", 1120, 130);
        addUnit(ObjectType.Vessel, "FLASH-02", 1260, 140);
        addUnit(ObjectType.Valve, "VALV-01", 1030, 210);
        addUnit(ObjectType.Valve, "VALV-02", 1260, 80);
        addUnit(ObjectType.ComponentSeparator, "DEMETH", 1450, 150);
        addUnit(ObjectType.ComponentSeparator, "DEETH", 1780, 200);

        connect("NG-Feed", "COM-01");
        connect("E-1", "COM-01");
        connect("COM-01", "S-02");
        connect("S-02", "SPLIT-01");

        connectPort("SPLIT-01", "S-03", 0, 0);
        connectPort("SPLIT-01", "S-04", 1, 0);

        connect("S-03", "MIX-01");
        connect("S-04", "COOL-01");
        connect("E-02", "COOL-01");
        connect("COOL-01", "S-05");
        connect("S-05", "COOL-02");
        connect("E-03", "COOL-02");
        connect("COOL-02", "S-06");
        connect("S-06", "MIX-01");
        connect("MIX-01", "S-07");
        connect("S-07", "HEX-01");

        connectPort("HEX-01", "S-08", 0, 0);
        connectPort("HEX-01", "S-10", 1, 0);

        connect("S-08", "COOL-03");
        connect("E-04", "COOL-03");
        connect("COOL-03", "S-11");
        connect("S-11", "FLASH-01");
        connect("E-05", "FLASH-01");

        connectPort("FLASH-01", "S-12", 0, 0);
        connectPort("FLASH-01", "S-13", 1, 0);

        connect("S-12", "SPLIT-02");
        connectPort("SPLIT-02", "S-15", 0, 0);
        connectPort("SPLIT-02", "S-14", 1, 0);

        connect("S-15", "HEX-02");
        connect("S-14", "EXP-01");
        connect("EXP-01", "S-19");
        connect("S-19", "FLASH-02");
        connect("E-08", "FLASH-02");

        connectPort("FLASH-02", "S-21", 0, 0);
        connectPort("FLASH-02", "S-22", 1, 0);

        connect("S-13", "VALV-01");
        connect("VALV-01", "S-23");

        connectPort("HEX-02", "S-17", 0, 0);
        connectPort("HEX-02", "S-16", 1, 0);

        connect("S-17", "VALV-02");
        connect("VALV-02", "S-20");

        connect("S-20", "DEMETH");
        connect("S-21", "DEMETH");
        connect("S-22", "DEMETH");
        connect("S-23", "DEMETH");

        connectPort("DEMETH", "S-24", 0, 0);
        connectPort("DEMETH", "S-25", 1, 0);

        connect("S-24", "Methane");

        connect("S-25", "DEETH");
        connectPort("DEETH", "Ethane", 0, 0);
        connectPort("DEETH", "Deeth_Btm", 1, 0);

        var feed = (MaterialStream)fs.GetFlowsheetSimulationObject("NG-Feed");
        feed.SetTemperature(310.0);
        feed.SetPressure(59.7818 * 100000.0);
        feed.SetMolarFlow(1464.0 / 3600.0);

        var z = new[]
        {
            0.8640,
            0.0647,
            0.0287,
            0.0072,
            0.0082,
            0.0041,
            0.0031,
            0.0031,
            0.0015,
            0.0154
        };
        feed.SetOverallComposition(z);

        var com1 = (Compressor)fs.GetFlowsheetSimulationObject("COM-01");
        com1.CalcMode = Compressor.CalculationMode.Delta_P;
        com1.DeltaP = 202650.0;
        com1.Efficiency = 0.75;

        var split1 = (Splitter)fs.GetFlowsheetSimulationObject("SPLIT-01");
        split1.SplitRatios[0] = 15.83 / 1464.0;
        split1.SplitRatios[1] = 1448.0 / 1464.0;

        var cool1 = (Cooler)fs.GetFlowsheetSimulationObject("COOL-01");
        cool1.CalcMode = Cooler.CalculationMode.OutletTemperature;
        cool1.OutletTemperature = 305.3;
        cool1.DeltaP = 20000.0;

        var cool2 = (Cooler)fs.GetFlowsheetSimulationObject("COOL-02");
        cool2.CalcMode = Cooler.CalculationMode.OutletTemperature;
        cool2.OutletTemperature = 289.7;
        cool2.DeltaP = 20000.0;

        var hex1 = (HeatExchanger)fs.GetFlowsheetSimulationObject("HEX-01");
        hex1.OverallCoefficient = 1000.0;
        hex1.Area = 239.2836;

        var cool3 = (Cooler)fs.GetFlowsheetSimulationObject("COOL-03");
        cool3.CalcMode = Cooler.CalculationMode.OutletTemperature;
        cool3.OutletTemperature = 251.4;
        cool3.DeltaP = 0.0;

        var flash1 = (Vessel)fs.GetFlowsheetSimulationObject("FLASH-01");
        flash1.PressureCalculation = PressureBehavior.Minimum;

        var split2 = (Splitter)fs.GetFlowsheetSimulationObject("SPLIT-02");
        split2.SplitRatios[0] = 483.3 / 1381.0;
        split2.SplitRatios[1] = 898.0 / 1381.0;

        var hex2 = (HeatExchanger)fs.GetFlowsheetSimulationObject("HEX-02");
        hex2.OverallCoefficient = 1000.0;
        hex2.Area = 804.7164;

        var exp1 = (Expander)fs.GetFlowsheetSimulationObject("EXP-01");
        exp1.CalcMode = Expander.CalculationMode.Delta_P;
        exp1.DeltaP = 3577572.5;
        exp1.Efficiency = 0.75;

        var flash2 = (Vessel)fs.GetFlowsheetSimulationObject("FLASH-02");
        flash2.PressureCalculation = PressureBehavior.Minimum;

        var v1 = (Valve)fs.GetFlowsheetSimulationObject("VALV-01");
        v1.OutletPressure = 26.4133 * 100000.0;

        var v2 = (Valve)fs.GetFlowsheetSimulationObject("VALV-02");
        v2.OutletPressure = 27.0083 * 100000.0;

        var demeth = (ComponentSeparator)fs.GetFlowsheetSimulationObject("DEMETH");
        demeth.ComponentSplits["Methane"] = 0.995;
        demeth.ComponentSplits["Nitrogen"] = 0.995;
        demeth.ComponentSplits["Ethane"] = 0.08;
        demeth.ComponentSplits["Propane"] = 0.01;
        demeth.ComponentSplits["Isobutane"] = 0.001;
        demeth.ComponentSplits["n-Butane"] = 0.001;
        demeth.ComponentSplits["Isopentane"] = 0.0001;
        demeth.ComponentSplits["n-Pentane"] = 0.0001;
        demeth.ComponentSplits["n-Hexane"] = 0.0001;
        demeth.ComponentSplits["n-Heptane"] = 0.0001;

        var deeth = (ComponentSeparator)fs.GetFlowsheetSimulationObject("DEETH");
        deeth.ComponentSplits["Methane"] = 0.99;
        deeth.ComponentSplits["Ethane"] = 0.96;
        deeth.ComponentSplits["Propane"] = 0.05;
        deeth.ComponentSplits["Isobutane"] = 0.001;
        deeth.ComponentSplits["n-Butane"] = 0.001;
        deeth.ComponentSplits["Isopentane"] = 0.0001;
        deeth.ComponentSplits["n-Pentane"] = 0.0001;
        deeth.ComponentSplits["n-Hexane"] = 0.0001;
        deeth.ComponentSplits["n-Heptane"] = 0.0001;
        deeth.ComponentSplits["Nitrogen"] = 0.95;

        fs.RequestCalculation();
        sim.SaveFlowsheet(fs, outputFile, true);
    }
}
