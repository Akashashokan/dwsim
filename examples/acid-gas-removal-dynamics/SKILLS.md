# DWSIM Flowsheet Generation — Agent Skills Reference

Everything an AI agent must know to generate a valid DWSIM flowsheet programmatically
using `DWSIM.Automation.Automation3` and `IFlowsheet.ConnectObjects`.

---

## 1. The One Rule That Governs All Connections

```
UnitOp → MaterialStream → UnitOp → MaterialStream → ...
```

Every connection in DWSIM must alternate between streams and unit operations.
**Never connect two unit operations directly. Never connect two streams directly.**

| From → To | Allowed? |
|---|---|
| MaterialStream → UnitOp | ✓ |
| UnitOp → MaterialStream | ✓ |
| MaterialStream → MaterialStream | ✗ throws "This connection is not allowed" |
| UnitOp → UnitOp | ✗ throws "This connection is not allowed" |
| MaterialStream → EnergyStream | ✗ |
| EnergyStream → MaterialStream | ✗ |

If two unit operations need to be connected, there must always be a `MaterialStream`
object between them — even if that stream has no physical meaning beyond being a connector.

---

## 2. ConnectObjects Signature

```csharp
sim.ConnectObjects(IGraphicObject from, IGraphicObject to, int fromIdx, int toIdx)
```

- `fromIdx` — which **output connector** of the FROM object to use
- `toIdx`   — which **input connector** of the TO object to use

### MaterialStream connector indices

A `MaterialStream` has **exactly one output connector** (index 0) and one input connector (index 0).

```csharp
// Connecting a stream to a unit op:
sim.ConnectObjects(stream.GraphicObject, unitOp.GraphicObject, 0, N);
//                                                             ^ always 0 for streams
```

Using `fromIdx = 1` on a `MaterialStream` is an out-of-range access and will fail.

---

## 3. Port Layout for Each Object Type

### Mixer
- **Inputs** [0, 1, 2, …]: feed streams (up to the configured number of inlets)
- **Output** [0]: mixed outlet stream

### Vessel (flash drum / separator)
- **Input**  [0]: feed stream
- **Output** [0]: vapour outlet
- **Output** [1]: liquid outlet

### Pump
- **Input**  [0]: feed stream (material)
- **Input**  [1]: energy stream (optional)
- **Output** [0]: discharge stream (material)
- **Output** [1]: energy stream (optional)

### Cooler / Heater
- **Input**  [0]: feed stream
- **Output** [0]: outlet stream

### AbsorptionColumn
- **Input**  [0]: gas feed (enters at the **bottom**)
- **Input**  [1]: liquid solvent feed (enters at the **top**)
- **Output** [0]: treated gas (leaves from the **top**)
- **Output** [1]: rich solvent (leaves from the **bottom**)

```csharp
// Correct wiring for an absorber:
sim.ConnectObjects(gasStream.GraphicObject,   absorber.GraphicObject, 0, 0); // gas in  (bottom)
sim.ConnectObjects(solventStream.GraphicObject, absorber.GraphicObject, 0, 1); // liquid in (top)
sim.ConnectObjects(absorber.GraphicObject, cleanGasStream.GraphicObject,  0, 0); // gas out  (top)
sim.ConnectObjects(absorber.GraphicObject, richAmine.GraphicObject,       1, 0); // liquid out (bottom)
```

### DistillationColumn
- **Input**  [0]: feed stream
- **Output** [0]: distillate / overhead vapour
- **Output** [1]: bottoms liquid

### RefluxedAbsorber / ReboiledAbsorber
Same port layout as `DistillationColumn` for material streams;
additionally accepts energy streams on dedicated energy connectors.

---

## 4. Mandatory Pattern: No Unit-Op-to-Unit-Op Shortcuts

Every link in the chain must go through a named `MaterialStream`.
Always declare an explicit stream object for each segment:

```csharp
// WRONG — pump directly to mixer:
sim.ConnectObjects(pump.GraphicObject, mixer.GraphicObject, 0, 0); // ✗

// CORRECT — pump → stream → mixer:
var pumpOut = sim.AddObject(ObjectType.MaterialStream, x, y, "Pump discharge");
sim.ConnectObjects(pump.GraphicObject,  pumpOut.GraphicObject, 0, 0);
sim.ConnectObjects(pumpOut.GraphicObject, mixer.GraphicObject, 0, 0);
```

---

## 5. Object Creation Order

1. **Add all unit operation objects** (`AddObject`)
2. **Add all stream objects** (`AddObject`)
3. **Call `PositionConnectors()`** on every graphic object
4. **Call `ConnectObjects()`** for every link

Calling `PositionConnectors()` before connecting ensures connector slots are
initialised in the correct positions.

```csharp
foreach (var o in sim.SimulationObjects.Values)
    ((dynamic)o.GraphicObject).PositionConnectors();
// Now safe to call ConnectObjects
```

---

## 6. Recycle Streams and Convergence

Any flowsheet that has a **closed loop** (output of a downstream unit feeding back
upstream) will loop forever unless a `RecycleOp` (tear stream) is inserted:

```csharp
var recycle = sim.AddObject(ObjectType.OT_Recycle, x, y, "Lean amine recycle");
```

Place the `RecycleOp` at **one** point in each loop. DWSIM uses it to converge
the loop by iterating between a guess and the calculated value.

Without a `RecycleOp` in every loop, the steady-state solver runs indefinitely.
The dynamic integrator may bypass this, but the flowsheet will give physically
meaningless results.

---

## 7. Dynamic Mode Setup

```csharp
sim.DynamicMode = true;

var integ = new Integrator {
    ID = Guid.NewGuid().ToString(),
    IntegrationStep           = TimeSpan.FromSeconds(5),
    Duration                  = TimeSpan.FromHours(24),   // keep short for laptop runs
    CalculationRateControl    = 1,
    CalculationRateEquilibrium = 5,
    CalculationRatePressureFlow = 1,
    RealTime      = false,
    RealTimeStepMs = 1000
};

var schedule = new Schedule {
    ID = Guid.NewGuid().ToString(),
    CurrentIntegrator        = integ.ID,
    UseCurrentStateAsInitial = true,
    UsesEventList            = false,
    UsesCauseAndEffectMatrix = false,
    ResetContentsOfAllObjects = false
};

sim.DynamicsManager.IntegratorList.Add(integ.ID, integ);
sim.DynamicsManager.ScheduleList.Add(schedule.ID, schedule);
sim.DynamicsManager.CurrentSchedule = schedule.ID;
```

---

## 8. Monitored Variables (KPIs)

A `MonitoredVariable` is attached to the `Integrator`, not to the flowsheet:

```csharp
integ.MonitoredVariables.Add(new MonitoredVariable {
    ID           = Guid.NewGuid().ToString(),
    ObjectID     = stream.Name,          // use .Name, not .GraphicObject.Tag
    PropertyID   = "PROP_MS_2",          // mass flow
    PropertyUnits = "kg/s",
    Description  = "Feed mass flow"
});
```

**Common property IDs for MaterialStream:**

| ID | Meaning |
|---|---|
| `PROP_MS_0` | Temperature |
| `PROP_MS_1` | Pressure |
| `PROP_MS_2` | Mass flow |
| `PROP_MS_7` | Molar flow |
| `PROP_MS_27` | Vapour fraction |
| `PROP_MS_106/<Compound name>` | Mole fraction of compound |
| `PROP_MS_102/<Compound name>` | Mass fraction of compound |

Always call `obj.GetProperties(PropertyType.ALL)` and verify a property ID exists
before adding it to `MonitoredVariables`; unknown IDs will throw at runtime.

### Scale limit on a laptop

| 5s interval, 5-year run | 31.5 million steps |
|---|---|
| 50,000 vars × 31.5M steps × 8 bytes | ~12.6 TB in RAM |

**Practical limit:** ~100–500 monitored variables. For larger datasets, write to
disk in the post-step Python script and do **not** store history in the integrator.

---

## 9. Python Scripts (Pre/Post Step)

Scripts are attached to the `Integrator` via `IFlowsheet.Scripts`:

```csharp
sim.Scripts.Add(script.ID, new Script {
    ID          = Guid.NewGuid().ToString(),
    Title       = "Feed ramp (pre-step)",
    Linked      = true,
    LinkedObjectType = Scripts.ObjectType.Integrator,
    LinkedEventType  = Scripts.EventType.IntegratorPreStep,
    PythonInterpreter = Scripts.Interpreter.IronPython,
    ScriptText  = File.ReadAllText("my_script.py")
});
```

`File.ReadAllText` uses `Environment.CurrentDirectory`. The Program.cs entry point
must `Directory.SetCurrentDirectory(scriptDir)` **before** calling `Generate()`.

Available event types:
- `IntegratorPreStep` — runs before each time step (use for feed manipulation)
- `IntegratorStep` — runs after each time step (use for KPI logging)

---

## 10. DWSIM.sln Project Registration

When adding a new C# project to the solution:

1. Add one `Project(...)...EndProject` block — **exactly one**, never duplicated.
2. The GUID in the `.sln` must match `<ProjectGuid>` in the `.csproj`.
3. For an `AnyCPU` project, map **all** platform slots to `Debug|Any CPU` / `Release|Any CPU`:

```
{GUID}.Debug|x64.ActiveCfg = Debug|Any CPU     ← NOT Debug|x64
{GUID}.Debug|x64.Build.0   = Debug|Any CPU
```

Mapping `Debug|x64 → Debug|x64` for an AnyCPU project causes Visual Studio to
report "startup project cannot be launched" because that configuration does not
exist in the `.csproj`.

---

## 11. Output Path and Assembly Resolution

The standard output path for a console helper project co-located with DWSIM:

```xml
<!-- Debug -->
<OutputPath>..\..\DWSIM\bin\x64\Debug\</OutputPath>

<!-- Release -->
<OutputPath>..\..\DWSIM\bin\Release\</OutputPath>
```

This places the EXE **4 levels below the repo root**
(`DWSIM\bin\x64\Debug\` from `examples\<project>\`):

```csharp
// Correct relative path back to examples\ from the EXE location:
Path.Combine(exeDir, @"..\..\..\..\examples\<project-name>")
//                    ^ 4 segments up to repo root
```

Set `Directory.SetCurrentDirectory(exeDir)` **first** so DWSIM can resolve its own
assemblies via the `AssemblyResolve` event, then switch to `scriptDir` before reading
Python scripts.

---

## 12. Quick Validation Checklist

Before calling `automation.SaveFlowsheet(...)`, verify:

- [ ] Every unit operation has at least one incoming and one outgoing `MaterialStream`
- [ ] No two unit operations are connected directly (no stream object between them)
- [ ] No `fromIdx > 0` on a `MaterialStream` (streams have only one outlet: index 0)
- [ ] Every closed loop contains exactly one `RecycleOp`
- [ ] `PositionConnectors()` was called on all objects before any `ConnectObjects()` call
- [ ] `MonitoredVariable.ObjectID` is set to `obj.Name` (the internal GUID-based name), not the display tag
- [ ] Python script files are present in `scriptDir` at runtime
- [ ] `sim.DynamicMode = true` is set before adding the `Integrator`
