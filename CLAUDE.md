# CLAUDE.md — HXG_Extension_France

## Project Overview

This is a **Hexagon ESPRIT EDGE CAM software extension** written in **VB.NET**. The extension integrates into the ESPRIT EDGE ribbon UI and provides multiple CAM-oriented features to assist operators working with machining features and documents.

## Communication

- User communicates in **French**
- All code, comments, variable names, method names, and development artifacts must be in **English**

## Technology Stack

| Item | Value |
|------|-------|
| Language | VB.NET |
| Framework | .NET Framework 4.8 |
| Platform | x64 (64-bit only) |
| Project Type | Class Library (DLL) |
| IDE | Visual Studio 2022 (v17) |
| Extension System | MEF (Managed Extensibility Framework) |

## Project Structure

```
ESPRITEDGE.CloseAllEdge/               ← Repository root (git)
├── CLAUDE.md                          ← This file
├── HXG_Extension_France.sln          ← Solution file
├── DocumentationAPI/
│   ├── ESPRIT-API.chm                 ← Full API documentation (CHM)
│   └── CHM_Extracted/                 ← Extracted HTML documentation (~5,390 pages)
│       ├── ESPRIT-API.xml             ← API definition/schema
│       └── *.html                     ← Class references and tutorials
└── HXG_Extension_France/              ← Project folder
    ├── HXG_Extension_France.vbproj   ← Project file
    ├── Main.vb                        ← Extension entry point (orchestration only)
    ├── IFeature.vb                    ← Interface that all features must implement
    ├── Features/
    │   └── CloseAllOpenEdge.vb        ← Feature: close open edges on selected FeatureChains
    └── My Project/
        ├── AssemblyInfo.vb            ← Assembly metadata (version 1.0.0.0, © 2026)
        └── *.Designer.vb             ← Auto-generated files (do not edit)
```

> Each new feature gets its own `.vb` file implementing `IFeature`, and is registered in `Main.vb > SetupRibbon()`.

## Key References (DLL Dependencies)

- `ESPRIT.NetApi` — Main ESPRIT EDGE managed API
- `Interop.Esprit` — COM interop for ESPRIT application objects
- `Interop.EspritConstants` — ESPRIT constants (message types, object types, etc.)
- Standard .NET: System, System.ComponentModel.Composition, System.Windows.Forms, System.Drawing, etc.

## Build & Deployment

- **Build output** is automatically deployed to:
  `%PUBLIC%\Documents\Hexagon\ESPRIT EDGE\Data\Extensions\HXG_Extension_France\`
- **Debug start program:** `%ProgramW6432%\Hexagon\ESPRIT EDGE\Prog\ESPRITEDGE.exe`
- Build configurations: `Debug|x64` and `Release|x64`

## Extension Architecture

The extension implements `ESPRIT.NetApi.Extensions.IExtension` via MEF export.

### Pattern: IFeature

Each feature implements `IFeature` (defined in `IFeature.vb`):

| Method | Role |
|--------|------|
| `Setup(tab As IRibbonTab)` | Adds the feature's ribbon group/button to the extension tab |
| `HandleButtonClick(e) As Boolean` | Handles click; returns `True` if handled (stops propagation) |
| `Disconnect()` | Releases any per-feature resources |

Features receive `ESPRIT.Application` via constructor injection.
To add a new feature: create `MyFeature.vb`, implement `IFeature`, register in `Main.vb > SetupRibbon()`.

### Main.vb — Orchestration Only

| Method | Role |
|--------|------|
| `Connect(app As Object)` | Entry point — instantiates features, calls `SetupRibbon()` |
| `Disconnect()` | Calls `Disconnect()` on each feature, removes ribbon tab |
| `SetupRibbon()` | Creates extension tab, calls `Setup(tab)` on each feature |
| `OnRibbonButtonClick` | Iterates features and calls `HandleButtonClick` until one handles it |

### Extension Metadata

| Property | Value |
|----------|-------|
| Name | "HXG_Extension_France" |
| Publisher | "ESPRIT EDGE" |
| SupportBuild | 20 |
| Url | http://www.espritcam.com |

## ESPRIT API Key Objects

| Object | Description |
|--------|-------------|
| `ESPRIT.Application` | Root application object (passed to `Connect`) |
| `ESPRIT.Document` | Active document |
| `Document.Group` | Currently selected items collection |
| `EspritConstants.espObjectType` | Enum used to identify object types (e.g., FeatureChain) |
| `EspritConstants.espMessageType` | Enum for EventWindow message severity |
| Ribbon objects | `RibbonTab`, `RibbonGroup`, `RibbonButton` via `ESPRIT.NetApi.Ribbon` |

## API Documentation

All ESPRIT EDGE API documentation is available in:
- `DocumentationAPI/ESPRIT-API.chm` — compiled help file (open with Windows)
- `DocumentationAPI/CHM_Extracted/` — extracted HTML (searchable with Grep)
  - Tutorial files: `*_tutorial.html`
  - Class references: `class*.html`

When looking up API classes or methods, search in `DocumentationAPI/CHM_Extracted/` using Grep before asking.

## Compiler Settings

- `Option Explicit: On`
- `Option Strict: Off` (allows late binding — used intentionally for COM interop)
- `Option Infer: On`
- Treat Warnings As Errors: Yes
