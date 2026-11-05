# 管网维修

Windows desktop maintenance record application built with WPF and .NET 8.

## Features

- Automatic daily order numbering
- Order format `JLYL-year-GWB-MMDD-sequence` with multi-window reservation
- Required-field completion tracking
- Parked, incomplete and completed ledger states
- Debounced real-time autosave after the first manual save
- Editable repair method, repair person and labor company options
- Imported material catalog with keyword search, multi-select and automatic prices
- Similar-material suggestions and automatic material template synchronization
- Ledger filters by date, repair person, repair method and status
- SQLite record persistence
- Installer-confirmed data directory: `D:\管网维修数据` when D exists, otherwise Desktop
- Automatic one-time migration of the existing local database to the selected folder
- Four managed data folders: `今日`, `维修记录`, `库`, `图片`
- Photo hyperlinks in the Excel ledger and a dedicated clickable photo-list worksheet
- One-click removable-drive backup with incremental copying and SHA-256 verification
- One-click removable-drive import with a pre-import database safety copy and integrity check
- Exit confirmation reminding the user to keep an up-to-date backup
- iOS-inspired glass UI with rounded translucent cards, input controls and jelly-style buttons
- 26-column Excel ledger export
- Single-sheet A4 landscape PDF output containing the complete record, photos and acceptance
- Daily portrait A4 PDF with today's events and all unfinished records
- Modern two-column section hierarchy, photo auto-fit, cost summary and page numbering

## Build

Prerequisites:

- Windows 10/11 x64
- .NET 8 SDK
- Inno Setup 6

```powershell
.\build-maintenance-record.ps1
```

The self-contained application is published to:

```text
MaintenanceRecordApp\Publish\win-x64
```

The Inno Setup installer is written to:

```text
Builds\MaintenanceRecord-Setup-4.1.0.exe
```

The installer lets the user confirm the data location:

```text
D:\管网维修数据
```

When the D drive is unavailable, the default location is:

```text
%USERPROFILE%\Desktop\管网维修数据
```

The selected folder contains:

```text
今日\       今日情况 PDF
维修记录\   单项维修记录 PDF
库\         Excel 台账、材料库和安全数据库副本
图片\       按工单和照片类别归档的施工图片
```
