# Natural Gas Separation - Simple to Deethanizer

This example adds a standalone C# automation generator project (mirroring the style used in `examples/acid-gas-removal-dynamics`) that builds and solves a simple natural-gas process ending with surrogate de-methanizer/de-ethanizer separators.

## Included files

- `GenerateFlowsheet.csproj` - .NET Framework 4.8 project with DWSIM references.
- `build_flowsheet_template.cs` - automation template (`NaturalGasSimpleFlowsheet.Generate`).
- `Program.cs` - console entry point that writes a `.dwxmz` file.

## Usage

Build and run the project, optionally passing an output path:

```bash
GenerateFlowsheet.exe C:\Temp\NG_Simple_Deethanizer.dwxmz
```

If no output path is provided, the generator writes:

- `natural-gas-simple-deethanizer.dwxmz` in the current working directory.
