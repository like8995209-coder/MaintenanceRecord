<p align="center">
  <img src="MaintenanceRecordApp/Assets/app.png" width="96" alt="管网维修图标" />
</p>

<h1 align="center">管网维修</h1>

<p align="center">
  面向管网维修现场的登记、费用核算、台账管理和资料归档工具
</p>

<p align="center">
  <img alt="版本 6.6.6" src="https://img.shields.io/badge/version-6.6.6-176B5B?style=flat-square" />
  <img alt="Windows 7 SP1 x64" src="https://img.shields.io/badge/Windows-7%20SP1%20x64%20%7C%2010%20%7C%2011-0078D4?style=flat-square" />
  <img alt=".NET 6 WPF" src="https://img.shields.io/badge/.NET%206-WPF-512BD4?style=flat-square" />
  <img alt="本地优先" src="https://img.shields.io/badge/data-local--first-E59B32?style=flat-square" />
</p>

<p align="center">
  <a href="https://github.com/like8995209-coder/MaintenanceRecord/releases/latest"><strong>下载最新版</strong></a>
  ·
  <a href="https://github.com/like8995209-coder/MaintenanceRecord/issues">问题反馈</a>
  ·
  <a href="#从源码构建">从源码构建</a>
</p>

![管网维修 - 维修记录登记](docs/images/new-record.png)

## 程序介绍

“管网维修”是一套面向管网维护、抢修和日常巡检场景的 Windows 桌面程序。它将现场维修信息、人工和机械工时、材料领用、路面还原费用、施工照片以及维修台账集中到一处，并在本机自动归档，不依赖云端服务。

程序针对现场录入做了简化：固定格式的维修编号自动生成，费用随人数、工时和尺寸实时计算，未完成的记录可以先挂单，资料补齐后再继续处理。维修记录可导出为 A4 PDF，台账可导出为 Excel，日常数据可通过 U 盘一键备份。

## 核心功能

| 功能 | 说明 |
| --- | --- |
| 维修登记 | 记录工单来源、人员、地点、原因、维修方式、时间、验收和制单信息 |
| 自动费用核算 | 人工按人数、小时单价和工时计算；机械按小时单价和工时计算 |
| 还原工程量 | 按长 × 宽 × 高计算体积，再结合每立方米单价自动计算还原费用 |
| 台账管理 | 按日期、人员、维修方式和状态筛选；支持挂单、未完成和已完成状态 |
| 材料库 | 导入 Excel 材料表，按名称、规格、单位和价格统一管理 |
| A4 PDF | 输出单项维修记录和今日情况报告，自动排版施工照片与费用清单 |
| Excel 台账 | 导出筛选后的维修台账和可点击的照片清单 |
| 一键 U 盘备份 | 增量复制本机数据到 U 盘，并逐个文件校验完整性 |
| 本地数据 | SQLite 本地存储，程序不上传维修资料或个人数据 |

## 界面预览

<p align="center">
  <img src="docs/images/records.png" width="49%" alt="台账记录界面" />
  <img src="docs/images/materials.png" width="49%" alt="材料库界面" />
</p>

<p align="center">
  <img src="docs/images/options.png" width="74%" alt="选项管理界面" />
</p>

## 使用流程

1. 新建维修记录，填写现场基础信息。
2. 选择人工、机械和还原类型，输入工时或长宽高，费用自动计算。
3. 从材料库选择领用材料，补充施工照片。
4. 保存台账，或先挂单以便后续继续填写。
5. 导出维修记录 PDF、今日情况 PDF 或 Excel 台账。
6. 将 U 盘插入电脑，使用“一键 U 盘备份”同步并校验数据。

## 系统要求

- Windows 7 SP1 x64、Windows 8.1 x64、Windows 10 或 Windows 11
- 64 位处理器
- 建议至少 4 GB 内存
- 安装包已包含 .NET 运行时，无需单独安装 .NET

> Windows 7 必须在 64 位系统上安装 SP1。当前程序使用 .NET 6 以兼容 Win7，安装包构建时会出现 .NET 6 已停止官方支持的安全提示；该提示不影响程序运行。

## 安装说明

1. 打开 [最新版本](https://github.com/like8995209-coder/MaintenanceRecord/releases/latest)，下载 `MaintenanceRecord-Setup-6.6.6.exe`。
2. 运行安装程序，确认数据库存储位置。
3. 默认优先使用 `D:\管网维修数据`；没有 D 盘时使用桌面下的 `管网维修数据`。
4. 安装完成后，可通过桌面或开始菜单中的“管网维修”启动。

安装包当前未进行商业代码签名。Windows 可能显示“未知发布者”或 SmartScreen 提示，请确认文件来自本仓库 Release 后再运行。

## 数据与备份

默认数据目录包含：

```text
今日\       今日情况 PDF
维修记录\   单项维修记录 PDF
库\         Excel 台账、材料库和安全数据库副本
图片\       按工单和照片类别归档的施工图片
```

所有业务数据保存在本机。建议每次收工前使用“一键 U 盘备份”，备份完成后通过 Windows 的“安全删除硬件并弹出媒体”功能拔出 U 盘。

## 6.6.6 版本说明

- 新增按小时核算人工和机械费用，瓦工默认单价为 ¥25/小时。
- 新增按长 × 宽 × 高自动计算路面还原工程量与费用。
- 合并 U 盘操作入口，统一为“一键 U 盘备份”。
- 精简未使用的运行时组件和非必要语言资源，安装包体积减少约 18.9%。
- 统一程序、安装器和界面版本号为 6.6.6。
- 完成数据库、材料导入、费用计算、台账筛选、PDF/Excel 输出和 U 盘备份自动化测试。

## 技术栈

- C# / .NET 6 / WPF
- SQLite / Microsoft.Data.Sqlite
- ClosedXML
- PDFsharp
- Inno Setup
- xUnit

## 从源码构建

前置条件：

- Windows 10/11 x64
- .NET 8 SDK
- Inno Setup 6

```powershell
git clone https://github.com/like8995209-coder/MaintenanceRecord.git
cd MaintenanceRecord
.\build-maintenance-record.ps1
```

构建脚本会先运行测试，然后生成自包含发布目录和安装包：

```text
MaintenanceRecordApp\Publish\win-x64
Builds\MaintenanceRecord-Setup-6.6.6.exe
```

程序截图由 `tools/ScreenshotRenderer` 根据演示数据库生成，不包含真实业务数据。

## 版权与使用

本仓库用于程序介绍、版本发布和问题反馈。当前未声明开源许可证；未经作者书面许可，不得复制、再发布或用于商业二次分发。
