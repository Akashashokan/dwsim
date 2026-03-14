"""
Extracts simulation components, streams, connections, compounds,
property packages, and thermodynamic properties from a .dwxmz file.

A .dwxmz file is a ZIP archive containing:
  - <uuid>.xml  — all simulation object definitions
  - <uuid>.db   — metadata SQLite database

Usage:
    python dwxmz_extractor.py <file.dwxmz> [--output json|text|summary]
    python dwxmz_extractor.py <file.dwxmz> --output json > result.json

Requirements:
    Python 3.7+ (standard library only)
"""

import sys
import json
import zipfile
import argparse
import xml.etree.ElementTree as ET
from collections import defaultdict
from dataclasses import dataclass, field, asdict
from typing import Optional, Tuple


# ---------------------------------------------------------------------------
# Data Classes
# ---------------------------------------------------------------------------

@dataclass
class Compound:
    name: str
    mole_fraction: float = 0.0
    mass_fraction: float = 0.0
    molar_flow: float = 0.0      # kmol/s
    mass_flow: float = 0.0       # kg/s
    volumetric_flow: float = 0.0 # m3/s
    volumetric_fraction: float = 0.0
    activity_coeff: float = 0.0
    fugacity_coeff: float = 0.0


@dataclass
class PhaseProperties:
    phase_id: int = 0
    phase_name: str = ""
    temperature: float = 0.0          # K
    pressure: float = 0.0             # Pa
    mass_flow: float = 0.0            # kg/s
    molar_flow: float = 0.0           # kmol/s
    volumetric_flow: float = 0.0      # m3/s
    density: float = 0.0              # kg/m3
    enthalpy: float = 0.0             # kJ/kg
    entropy: float = 0.0              # kJ/(kg·K)
    molar_enthalpy: float = 0.0       # kJ/kmol
    molar_entropy: float = 0.0        # kJ/(kmol·K)
    heat_capacity_cp: float = 0.0     # kJ/(kg·K)
    heat_capacity_cv: float = 0.0     # kJ/(kg·K)
    molecular_weight: float = 0.0     # kg/kmol
    viscosity: float = 0.0            # Pa·s
    thermal_conductivity: float = 0.0 # W/(m·K)
    vapor_fraction: float = 0.0
    compounds: list = field(default_factory=list)


PHASE_NAMES = {
    0: "Overall (Mixture)",
    1: "Overall Liquid",
    2: "Vapor",
    3: "Liquid 1",
    4: "Liquid 2",
    5: "Liquid 3",
    6: "Aqueous",
    7: "Solid",
}


@dataclass
class SimulationObject:
    id: str
    tag: str = ""
    type: str = ""
    object_class: str = ""
    description: str = ""
    property_package: str = ""
    calculated: bool = False
    active: bool = True
    # Positional info from graphic object
    x: float = 0.0
    y: float = 0.0
#!/usr/bin/env python3
"""
DWSIM .dwxmz File Extractor
Extracts simulation components, streams, connections, compounds,
property packages, and thermodynamic properties from a .dwxmz file.

A .dwxmz file is a ZIP archive containing:
  - <uuid>.xml  — all simulation object definitions
  - <uuid>.db   — metadata SQLite database

Usage:
    python dwxmz_extractor.py <file.dwxmz> [--output json|text|summary]
    python dwxmz_extractor.py <file.dwxmz> --output json > result.json

Requirements:
    Python 3.7+ (standard library only)
"""

import sys
import json
import zipfile
import argparse
import xml.etree.ElementTree as ET
from collections import defaultdict
from dataclasses import dataclass, field, asdict
from typing import Optional


# ---------------------------------------------------------------------------
# Data Classes
# ---------------------------------------------------------------------------

@dataclass
class Compound:
    name: str
    mole_fraction: float = 0.0
    mass_fraction: float = 0.0
    molar_flow: float = 0.0      # kmol/s
    mass_flow: float = 0.0       # kg/s
    volumetric_flow: float = 0.0 # m3/s
    volumetric_fraction: float = 0.0
    activity_coeff: float = 0.0
    fugacity_coeff: float = 0.0


@dataclass
class PhaseProperties:
    phase_id: int = 0
    phase_name: str = ""
    temperature: float = 0.0          # K
    pressure: float = 0.0             # Pa
    mass_flow: float = 0.0            # kg/s
    molar_flow: float = 0.0           # kmol/s
    volumetric_flow: float = 0.0      # m3/s
    density: float = 0.0              # kg/m3
    enthalpy: float = 0.0             # kJ/kg
    entropy: float = 0.0              # kJ/(kg·K)
    molar_enthalpy: float = 0.0       # kJ/kmol
    molar_entropy: float = 0.0        # kJ/(kmol·K)
    heat_capacity_cp: float = 0.0     # kJ/(kg·K)
    heat_capacity_cv: float = 0.0     # kJ/(kg·K)
    molecular_weight: float = 0.0     # kg/kmol
    viscosity: float = 0.0            # Pa·s
    thermal_conductivity: float = 0.0 # W/(m·K)
    vapor_fraction: float = 0.0
    compounds: list = field(default_factory=list)


PHASE_NAMES = {
    0: "Overall (Mixture)",
    1: "Overall Liquid",
    2: "Vapor",
    3: "Liquid 1",
    4: "Liquid 2",
    5: "Liquid 3",
    6: "Aqueous",
    7: "Solid",
}


@dataclass
class SimulationObject:
    id: str
    tag: str = ""
    type: str = ""
    object_class: str = ""
    description: str = ""
    property_package: str = ""
    calculated: bool = False
    active: bool = True
    # Positional info from graphic object
    x: float = 0.0
    y: float = 0.0
    # Extra type-specific properties (key-value pairs)
    extra: dict = field(default_factory=dict)
    # Full XML payload for this object (useful for Dynamic/CAPE-OPEN data)
    raw_data: dict = field(default_factory=dict)


@dataclass
class MaterialStream(SimulationObject):
    spec_type: str = ""
    composition_basis: str = ""
    defined_flow: str = ""
    force_phase: str = ""
    phases: list = field(default_factory=list)  # list of PhaseProperties


@dataclass
class EnergyStream(SimulationObject):
    energy_flow: float = 0.0  # kW


@dataclass
class UnitOperation(SimulationObject):
    pass


@dataclass
class Connector:
    is_attached: bool = False
    conn_type: str = ""
    from_obj_id: str = ""
    from_conn_index: int = 0
    to_obj_id: str = ""
    to_conn_index: int = 0
    is_energy: bool = False


@dataclass
class GraphicObject:
    name: str
    tag: str = ""
    object_type: str = ""
    description: str = ""
    x: float = 0.0
    y: float = 0.0
    width: float = 0.0
    height: float = 0.0
    calculated: bool = False
    active: bool = True
    input_connectors: list = field(default_factory=list)   # list of Connector
    output_connectors: list = field(default_factory=list)  # list of Connector
    energy_connector: Optional[Connector] = None


@dataclass
class Connection:
    """A resolved connection between two simulation objects."""
    from_obj_id: str
    from_obj_tag: str
    from_conn_index: int
    to_obj_id: str
    to_obj_tag: str
    to_conn_index: int
    is_energy: bool = False


@dataclass
class GlobalCompound:
    name: str
    cas_number: str = ""
    formula: str = ""
    molar_weight: float = 0.0        # kg/kmol
    critical_temperature: float = 0.0 # K
    critical_pressure: float = 0.0    # Pa
    critical_volume: float = 0.0      # m3/kmol
    acentric_factor: float = 0.0
    normal_boiling_point: float = 0.0 # K


@dataclass
class PropertyPackage:
    id: str
    type: str = ""
    name: str = ""
    description: str = ""


@dataclass
class Simulation:
    file_path: str = ""
    build_version: str = ""
    saved_on: str = ""
    os_info: str = ""
    # Components
    compounds: list = field(default_factory=list)           # GlobalCompound list
    property_packages: list = field(default_factory=list)   # PropertyPackage list
    material_streams: list = field(default_factory=list)    # MaterialStream list
    energy_streams: list = field(default_factory=list)      # EnergyStream list
    unit_operations: list = field(default_factory=list)     # UnitOperation list
    connections: list = field(default_factory=list)         # Connection list
    additional_sections: dict = field(default_factory=dict) # Unmapped root sections


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _text(el: ET.Element, tag: str, default="") -> str:
    child = el.find(tag)
    if child is not None and child.text:
        return child.text.strip()
    return default


def _float(el: ET.Element, tag: str, default=0.0) -> float:
    val = _text(el, tag)
    if not val:
        return default
    try:
        return float(val)
    except (ValueError, OverflowError):
        return default


def _bool(el: ET.Element, tag: str, default=False) -> bool:
    val = _text(el, tag).lower()
    if val in ("true", "1", "yes"):
        return True
    if val in ("false", "0", "no"):
        return False
    return default


def _int(el: ET.Element, tag: str, default=0) -> int:
    val = _text(el, tag)
    try:
        return int(val)
    except (ValueError, TypeError):
        return default


def element_to_dict(el: ET.Element) -> dict:
    """Convert an XML element tree into a nested dict preserving attributes/repetitions."""
    node: dict = {}

    if el.attrib:
        node["@attributes"] = dict(el.attrib)

    text = (el.text or "").strip()
    if text:
        node["#text"] = text

    children = list(el)
    if children:
        grouped = defaultdict(list)
        for child in children:
            grouped[child.tag].append(element_to_dict(child))

        for tag, values in grouped.items():
            node[tag] = values[0] if len(values) == 1 else values

    return node


def flatten_leaf_text(el: ET.Element, prefix: str = "") -> dict:
    """Collect all leaf text nodes using slash-separated paths as keys."""
    out = {}
    path = f"{prefix}/{el.tag}" if prefix else el.tag
    children = list(el)

    if not children:
        txt = (el.text or "").strip()
        if txt:
            out[path] = txt
        return out

    for child in children:
        out.update(flatten_leaf_text(child, path))
    return out


# ---------------------------------------------------------------------------
# Parsers
# ---------------------------------------------------------------------------

def parse_compound_in_phase(el: ET.Element) -> Compound:
    return Compound(
        name=_text(el, "Name") or _text(el, "ComponentName"),
        mole_fraction=_float(el, "MoleFraction"),
        mass_fraction=_float(el, "MassFraction"),
        molar_flow=_float(el, "MolarFlow"),
        mass_flow=_float(el, "MassFlow"),
        volumetric_flow=_float(el, "VolumetricFlow"),
        volumetric_fraction=_float(el, "VolumetricFraction"),
        activity_coeff=_float(el, "ActivityCoeff"),
        fugacity_coeff=_float(el, "FugacityCoeff"),
    )


def parse_phase(el: ET.Element) -> PhaseProperties:
    phase_id = _int(el, "ID")

    # There are TWO <Properties> elements: the first is just a type-name string,
    # the second contains the actual numeric data as child elements.
    props_el = None
    for candidate in el.findall("Properties"):
        # Pick the one that actually has child elements (the real data block)
        if len(candidate) > 0:
            props_el = candidate
            break

    phase = PhaseProperties(
        phase_id=phase_id,
        phase_name=PHASE_NAMES.get(phase_id, f"Phase {phase_id}"),
    )

    if props_el is not None:
        phase.temperature = _float(props_el, "temperature")
        phase.pressure = _float(props_el, "pressure")
        phase.mass_flow = _float(props_el, "massflow")
        phase.molar_flow = _float(props_el, "molarflow")
        phase.volumetric_flow = _float(props_el, "volumetric_flow")
        phase.density = _float(props_el, "density")
        phase.enthalpy = _float(props_el, "enthalpy")
        phase.entropy = _float(props_el, "entropy")
        phase.molar_enthalpy = _float(props_el, "molar_enthalpy")
        phase.molar_entropy = _float(props_el, "molar_entropy")
        phase.heat_capacity_cp = _float(props_el, "heatCapacityCp")
        phase.heat_capacity_cv = _float(props_el, "heatCapacityCv")
        phase.molecular_weight = _float(props_el, "molecularWeight")
        phase.viscosity = _float(props_el, "viscosity")
        phase.thermal_conductivity = _float(props_el, "thermalConductivity")
        phase.vapor_fraction = _float(props_el, "vapor_fraction")

    # Same pattern: two <Compounds> elements; pick the one with child elements
    for compounds_el in el.findall("Compounds"):
        if len(compounds_el) > 0:
            for cmp_el in compounds_el.findall("Compound"):
                phase.compounds.append(parse_compound_in_phase(cmp_el))
            break

    return phase


def parse_material_stream(el: ET.Element) -> MaterialStream:
    stream = MaterialStream(
        id=_text(el, "ComponentName") or _text(el, "Name"),
        type=_text(el, "Type"),
        object_class=_text(el, "ObjectClass"),
        description=_text(el, "ComponentDescription"),
        property_package=_text(el, "PropertyPackage"),
        calculated=_bool(el, "Calculated"),
        active=_bool(el, "Active", default=True),
        spec_type=_text(el, "SpecType"),
        composition_basis=_text(el, "CompositionBasis"),
        defined_flow=_text(el, "DefinedFlow"),
        force_phase=_text(el, "ForcePhase"),
        raw_data=element_to_dict(el),
    )
    phases_el = el.find("Phases")
    if phases_el is not None:
        for phase_el in phases_el.findall("Phase"):
            stream.phases.append(parse_phase(phase_el))
    return stream


def parse_energy_stream(el: ET.Element) -> EnergyStream:
    return EnergyStream(
        id=_text(el, "ComponentName") or _text(el, "Name"),
        type=_text(el, "Type"),
        object_class=_text(el, "ObjectClass"),
        description=_text(el, "ComponentDescription"),
        property_package=_text(el, "PropertyPackage"),
        calculated=_bool(el, "Calculated"),
        active=_bool(el, "Active", default=True),
        energy_flow=_float(el, "EnergyFlow"),
        raw_data=element_to_dict(el),
    )


# Unit operation type keywords → friendly class label
_UO_TYPE_MAP = {
    "Pump": "Pump",
    "Compressor": "Compressor",
    "Expander": "Expander",
    "Valve": "Valve",
    "Heater": "Heater",
    "Cooler": "Cooler",
    "Mixer": "Mixer",
    "Splitter": "Splitter",
    "HeatExchanger": "HeatExchanger",
    "Vessel": "Vessel",
    "Tank": "Tank",
    "Pipe": "Pipe",
    "RigorousColumn": "RigorousColumn",
    "ShortcutColumn": "ShortcutColumn",
    "Reactor": "Reactor",
    "ComponentSeparator": "ComponentSeparator",
    "SolidsSeparator": "SolidsSeparator",
    "Filter": "Filter",
    "Crystallizer": "Crystallizer",
    "OREGeneralizedUnit": "CustomUnit",
}

# Properties to always capture from unit operations
_COMMON_UO_PROPS = [
    "DeltaP", "DeltaT", "DeltaQ", "Efficiency", "Area",
    "OverallCoefficient", "PressureCalculation", "FlowSpec",
    "DutySpec", "HeatLoss", "Pressure", "Temperature",
    "SplitRatios", "NumberOfStages", "RefluxRatio",
    "BottomFlowRate", "ConversionSpec",
]


def _uo_friendly_type(full_type: str) -> str:
    for key, label in _UO_TYPE_MAP.items():
        if key in full_type:
            return label
    return full_type.split(".")[-1] if "." in full_type else full_type


def parse_unit_operation(el: ET.Element) -> UnitOperation:
    uo = UnitOperation(
        id=_text(el, "ComponentName") or _text(el, "Name"),
        type=_text(el, "Type"),
        object_class=_text(el, "ObjectClass"),
        description=_text(el, "ComponentDescription"),
        property_package=_text(el, "PropertyPackage"),
        calculated=_bool(el, "Calculated"),
        active=_bool(el, "Active", default=True),
        raw_data=element_to_dict(el),
    )
    # Capture common UO-specific properties
    for prop in _COMMON_UO_PROPS:
        val = _text(el, prop)
        if val:
            uo.extra[prop] = val

    # Capture *all* scalar leaf values (includes dynamic sim and CAPE-OPEN details
    # whenever they are present in the XML payload).
    known_core_fields = {
        "ComponentName", "Name", "Type", "ObjectClass", "ComponentDescription",
        "PropertyPackage", "Calculated", "Active",
    }
    for key, value in flatten_leaf_text(el).items():
        leaf_name = key.rsplit("/", 1)[-1]
        if leaf_name in known_core_fields:
            continue
        uo.extra.setdefault(key, value)
    return uo


def parse_connector_element(el: ET.Element) -> Connector:
    c = Connector()
    c.is_attached = el.get("IsAttached", "false").lower() == "true"
    c.conn_type = el.get("ConnType", "")
    c.from_obj_id = el.get("AttachedFromObjID", "")
    c.from_conn_index = int(el.get("AttachedFromConnIndex", "0") or 0)
    c.to_obj_id = el.get("AttachedToObjID", "")
    c.to_conn_index = int(el.get("AttachedToConnIndex", "0") or 0)
    c.is_energy = el.get("AttachedToEnergyConn", el.get("AttachedFromEnergyConn", "false")).lower() == "true"
    return c


def parse_graphic_object(el: ET.Element) -> GraphicObject:
    go = GraphicObject(
        name=_text(el, "Name"),
        tag=_text(el, "Tag"),
        object_type=_text(el, "ObjectType"),
        description=_text(el, "Description"),
        x=_float(el, "X"),
        y=_float(el, "Y"),
        width=_float(el, "Width"),
        height=_float(el, "Height"),
        calculated=_bool(el, "Calculated"),
        active=_bool(el, "Active", default=True),
    )

    for conn_el in el.findall("InputConnectors/Connector"):
        go.input_connectors.append(parse_connector_element(conn_el))

    for conn_el in el.findall("OutputConnectors/Connector"):
        go.output_connectors.append(parse_connector_element(conn_el))

    energy_el = el.find("EnergyConnector/Connector")
    if energy_el is not None:
        go.energy_connector = parse_connector_element(energy_el)

    return go


def parse_global_compound(el: ET.Element) -> GlobalCompound:
    return GlobalCompound(
        name=_text(el, "Name"),
        cas_number=_text(el, "CAS_Number"),
        formula=_text(el, "Formula"),
        molar_weight=_float(el, "Molar_Weight"),
        critical_temperature=_float(el, "Critical_Temperature"),
        critical_pressure=_float(el, "Critical_Pressure"),
        critical_volume=_float(el, "Critical_Volume"),
        acentric_factor=_float(el, "Acentric_Factor"),
        normal_boiling_point=_float(el, "NBP"),
    )


def parse_property_package(el: ET.Element) -> PropertyPackage:
    return PropertyPackage(
        id=_text(el, "ID") or _text(el, "ComponentName"),
        type=_text(el, "Type"),
        name=_text(el, "ComponentName"),
        description=_text(el, "ComponentDescription"),
    )


# ---------------------------------------------------------------------------
# Main Extraction
# ---------------------------------------------------------------------------

# Full .NET class → stream/unit-op category
_MATERIAL_STREAM_TYPE = "MaterialStream"
_ENERGY_STREAM_TYPE = "EnergyStream"


def is_material_stream(full_type: str) -> bool:
    return "MaterialStream" in full_type


def is_energy_stream(full_type: str) -> bool:
    return "EnergyStream" in full_type


def extract_xml_from_dwxmz(path: str) -> ET.Element:
    """Open a .dwxmz ZIP and return the parsed root XML element."""
    with zipfile.ZipFile(path, "r") as z:
        xml_names = [n for n in z.namelist() if n.lower().endswith(".xml")]
        if not xml_names:
            raise ValueError(f"No XML file found inside {path}")
        with z.open(xml_names[0]) as f:
            return ET.parse(f).getroot()


def extract(path: str) -> Simulation:
    """Full extraction of a .dwxmz file into a Simulation object."""
    root = extract_xml_from_dwxmz(path)
    sim = Simulation(file_path=path)

    # --- General Info ---
    gi = root.find("GeneralInfo")
    if gi is not None:
        sim.build_version = _text(gi, "BuildVersion")
        sim.saved_on = _text(gi, "SavedOn")
        sim.os_info = _text(gi, "OSInfo")

    # --- Compounds (global list) ---
    for cmp_el in root.findall("Compounds/Compound"):
        sim.compounds.append(parse_global_compound(cmp_el))

    # --- Property Packages ---
    for pp_el in root.findall("PropertyPackages/PropertyPackage"):
        sim.property_packages.append(parse_property_package(pp_el))

    # --- Simulation Objects ---
    for obj_el in root.findall("SimulationObjects/SimulationObject"):
        full_type = _text(obj_el, "Type")
        if is_material_stream(full_type):
            sim.material_streams.append(parse_material_stream(obj_el))
        elif is_energy_stream(full_type):
            sim.energy_streams.append(parse_energy_stream(obj_el))
        else:
            sim.unit_operations.append(parse_unit_operation(obj_el))

    # --- Preserve any extra top-level sections not explicitly mapped above ---
    mapped_sections = {
        "GeneralInfo", "Compounds", "PropertyPackages", "SimulationObjects", "GraphicObjects"
    }
    for child in root:
        if child.tag not in mapped_sections:
            sim.additional_sections[child.tag] = element_to_dict(child)

    # --- Graphic Objects → positions + connections ---
    graphic_map: dict[str, GraphicObject] = {}
    id_to_tag: dict[str, str] = {}

    for go_el in root.findall("GraphicObjects/GraphicObject"):
        go = parse_graphic_object(go_el)
        graphic_map[go.name] = go
        id_to_tag[go.name] = go.tag or go.name

    # Attach position to simulation objects
    all_sim_objects: list[SimulationObject] = (
        sim.material_streams + sim.energy_streams + sim.unit_operations  # type: ignore
    )
    for obj in all_sim_objects:
        go = graphic_map.get(obj.id)
        if go:
            obj.x = go.x
            obj.y = go.y
            obj.tag = go.tag

    # Build connection list from graphic objects (output connectors)
    seen_connections: set[tuple] = set()
    for go in graphic_map.values():
        for out_conn in go.output_connectors:
            if not out_conn.is_attached or not out_conn.to_obj_id:
                continue
            key = (go.name, out_conn.to_obj_id)
            if key in seen_connections:
                continue
            seen_connections.add(key)
            sim.connections.append(Connection(
                from_obj_id=go.name,
                from_obj_tag=id_to_tag.get(go.name, go.name),
                from_conn_index=out_conn.from_conn_index,
                to_obj_id=out_conn.to_obj_id,
                to_obj_tag=id_to_tag.get(out_conn.to_obj_id, out_conn.to_obj_id),
                to_conn_index=out_conn.to_conn_index,
                is_energy=out_conn.is_energy,
            ))

    return sim


# ---------------------------------------------------------------------------
# Output Formatters
# ---------------------------------------------------------------------------

def _unit(value: float, unit: str, fmt=".4g") -> str:
    return f"{value:{fmt}} {unit}"


def _overall_phase(stream: "MaterialStream") -> Optional[PhaseProperties]:
    for p in stream.phases:
        if p.phase_id == 0:
            return p
    return stream.phases[0] if stream.phases else None


def print_summary(sim: Simulation):
    print("=" * 70)
    print(f"  DWSIM Simulation: {sim.file_path}")
    print(f"  Version: {sim.build_version}   Saved: {sim.saved_on}")
    print("=" * 70)

    # Compounds
    print(f"\n[COMPOUNDS] ({len(sim.compounds)} total)")
    for c in sim.compounds:
        print(f"  • {c.name:<30} CAS: {c.cas_number:<14} MW: {c.molar_weight:.3f} kg/kmol")

    # Property Packages
    print(f"\n[PROPERTY PACKAGES] ({len(sim.property_packages)} total)")
    for pp in sim.property_packages:
        print(f"  • {pp.name}  [{pp.type.split('.')[-1]}]")

    # Material Streams
    print(f"\n[MATERIAL STREAMS] ({len(sim.material_streams)} total)")
    for s in sim.material_streams:
        phase = _overall_phase(s)
        status = "OK" if s.calculated else "?"
        tag = s.tag or s.id
        print(f"\n  Stream: {tag}  [{status}]  Spec: {s.spec_type}")
        if phase and phase.temperature:
            p_bar = phase.pressure / 1e5 if phase.pressure else 0
            print(f"    T={_unit(phase.temperature,'K')}  "
                  f"P={p_bar:.4f} bar  "
                  f"ṁ={_unit(phase.mass_flow,'kg/s')}  "
                  f"ṅ={_unit(phase.molar_flow,'kmol/s')}")
            if phase.compounds:
                print(f"    Composition ({s.composition_basis or 'Molar_Fractions'}):")
                for cmp in phase.compounds:
                    if cmp.mole_fraction > 1e-9:
                        print(f"      {cmp.name:<25} x={cmp.mole_fraction:.4f}  "
                              f"w={cmp.mass_fraction:.4f}")

    # Energy Streams
    if sim.energy_streams:
        print(f"\n[ENERGY STREAMS] ({len(sim.energy_streams)} total)")
        for s in sim.energy_streams:
            tag = s.tag or s.id
            status = "OK" if s.calculated else "?"
            print(f"  • {tag:<30} Q={_unit(s.energy_flow,'kW')}  [{status}]")

    # Unit Operations
    print(f"\n[UNIT OPERATIONS] ({len(sim.unit_operations)} total)")
    for uo in sim.unit_operations:
        tag = uo.tag or uo.id
        friendly = _uo_friendly_type(uo.type)
        status = "OK" if uo.calculated else "?"
        extra_str = ""
        if uo.extra:
            pairs = [f"{k}={v}" for k, v in list(uo.extra.items())[:3]]
            extra_str = "  " + ", ".join(pairs)
        print(f"  • {tag:<30} [{friendly}] [{status}]{extra_str}")

    # Connections / Topology
    print(f"\n[CONNECTIONS] ({len(sim.connections)} total)")
    for conn in sim.connections:
        kind = "~energy~" if conn.is_energy else "→"
        print(f"  {conn.from_obj_tag:<25} {kind}  {conn.to_obj_tag}")

    print()


def to_json_dict(sim: Simulation) -> dict:
    """Convert Simulation to a plain dict suitable for JSON serialization."""

    def phase_to_dict(p: PhaseProperties) -> dict:
        return {
            "phase_id": p.phase_id,
            "phase_name": p.phase_name,
            "temperature_K": p.temperature,
            "pressure_Pa": p.pressure,
            "pressure_bar": round(p.pressure / 1e5, 6) if p.pressure else 0,
            "mass_flow_kg_s": p.mass_flow,
            "molar_flow_kmol_s": p.molar_flow,
            "volumetric_flow_m3_s": p.volumetric_flow,
            "density_kg_m3": p.density,
            "enthalpy_kJ_kg": p.enthalpy,
            "entropy_kJ_kgK": p.entropy,
            "molar_enthalpy_kJ_kmol": p.molar_enthalpy,
            "molar_entropy_kJ_kmolK": p.molar_entropy,
            "heat_capacity_cp_kJ_kgK": p.heat_capacity_cp,
            "heat_capacity_cv_kJ_kgK": p.heat_capacity_cv,
            "molecular_weight_kg_kmol": p.molecular_weight,
            "viscosity_Pa_s": p.viscosity,
            "thermal_conductivity_W_mK": p.thermal_conductivity,
            "vapor_fraction": p.vapor_fraction,
            "compounds": [
                {
                    "name": c.name,
                    "mole_fraction": c.mole_fraction,
                    "mass_fraction": c.mass_fraction,
                    "molar_flow_kmol_s": c.molar_flow,
                    "mass_flow_kg_s": c.mass_flow,
                    "volumetric_flow_m3_s": c.volumetric_flow,
                    "volumetric_fraction": c.volumetric_fraction,
                    "activity_coeff": c.activity_coeff,
                    "fugacity_coeff": c.fugacity_coeff,
                }
                for c in p.compounds
            ],
        }

    def stream_to_dict(s: MaterialStream) -> dict:
        return {
            "id": s.id,
            "tag": s.tag,
            "type": s.type,
            "object_class": s.object_class,
            "description": s.description,
            "spec_type": s.spec_type,
            "composition_basis": s.composition_basis,
            "defined_flow": s.defined_flow,
            "force_phase": s.force_phase,
            "property_package": s.property_package,
            "calculated": s.calculated,
            "active": s.active,
            "position": {"x": s.x, "y": s.y},
            "phases": [phase_to_dict(p) for p in s.phases],
            "raw_data": s.raw_data,
        }

    def energy_stream_to_dict(s: EnergyStream) -> dict:
        return {
            "id": s.id,
            "tag": s.tag,
            "type": s.type,
            "object_class": s.object_class,
            "energy_flow_kW": s.energy_flow,
            "property_package": s.property_package,
            "calculated": s.calculated,
            "active": s.active,
            "position": {"x": s.x, "y": s.y},
            "raw_data": s.raw_data,
        }

    def uo_to_dict(uo: UnitOperation) -> dict:
        return {
            "id": uo.id,
            "tag": uo.tag,
            "type": uo.type,
            "friendly_type": _uo_friendly_type(uo.type),
            "object_class": uo.object_class,
            "description": uo.description,
            "property_package": uo.property_package,
            "calculated": uo.calculated,
            "active": uo.active,
            "position": {"x": uo.x, "y": uo.y},
            "properties": uo.extra,
            "raw_data": uo.raw_data,
        }

    def conn_to_dict(c: Connection) -> dict:
        return {
            "from": {"id": c.from_obj_id, "tag": c.from_obj_tag, "connector_index": c.from_conn_index},
            "to":   {"id": c.to_obj_id,   "tag": c.to_obj_tag,   "connector_index": c.to_conn_index},
            "is_energy_stream": c.is_energy,
        }

    return {
        "file": sim.file_path,
        "build_version": sim.build_version,
        "saved_on": sim.saved_on,
        "os_info": sim.os_info,
        "compounds": [
            {
                "name": c.name,
                "cas_number": c.cas_number,
                "formula": c.formula,
                "molar_weight_kg_kmol": c.molar_weight,
                "critical_temperature_K": c.critical_temperature,
                "critical_pressure_Pa": c.critical_pressure,
                "critical_volume_m3_kmol": c.critical_volume,
                "acentric_factor": c.acentric_factor,
                "normal_boiling_point_K": c.normal_boiling_point,
            }
            for c in sim.compounds
        ],
        "property_packages": [
            {
                "id": pp.id,
                "name": pp.name,
                "type": pp.type,
                "description": pp.description,
            }
            for pp in sim.property_packages
        ],
        "material_streams": [stream_to_dict(s) for s in sim.material_streams],
        "energy_streams":   [energy_stream_to_dict(s) for s in sim.energy_streams],
        "unit_operations":  [uo_to_dict(uo) for uo in sim.unit_operations],
        "connections":      [conn_to_dict(c) for c in sim.connections],
        "additional_sections": sim.additional_sections,
        "summary": {
            "n_compounds":        len(sim.compounds),
            "n_property_packages": len(sim.property_packages),
            "n_material_streams": len(sim.material_streams),
            "n_energy_streams":   len(sim.energy_streams),
            "n_unit_operations":  len(sim.unit_operations),
            "n_connections":      len(sim.connections),
        },
    }


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(
        description="Extract simulation data from a DWSIM .dwxmz file",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    parser.add_argument("file", help="Path to the .dwxmz file")
    parser.add_argument(
        "--output", "-o",
        choices=["summary", "json", "text"],
        default="summary",
        help="Output format (default: summary)",
    )
    args = parser.parse_args()

    try:
        sim = extract(args.file)
    except FileNotFoundError:
        print(f"Error: File not found: {args.file}", file=sys.stderr)
        sys.exit(1)
    except zipfile.BadZipFile:
        print(f"Error: Not a valid .dwxmz file (ZIP): {args.file}", file=sys.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"Error: {e}", file=sys.stderr)
        sys.exit(1)

    if args.output in ("summary", "text"):
        print_summary(sim)
    elif args.output == "json":
        print(json.dumps(to_json_dict(sim), indent=2, default=str))


if __name__ == "__main__":
    main()
