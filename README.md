# MobilePrint — 手机远程打印系统

[![Release](https://img.shields.io/github/v/release/liangleionline/MobilePrint?label=release&sort=semver)](https://github.com/liangleionline/MobilePrint/releases/latest)
[![Latest downloads](https://img.shields.io/github/downloads/liangleionline/MobilePrint/latest/total?label=latest%20downloads)](https://github.com/liangleionline/MobilePrint/releases/latest)
[![Total downloads](https://img.shields.io/github/downloads/liangleionline/MobilePrint/total?label=total%20downloads)](https://github.com/liangleionline/MobilePrint/releases)
[![Visitors](https://visitor-badge.laobi.icu/badge?page_id=liangleionline.MobilePrint)](https://github.com/liangleionline/MobilePrint)

[![Last commit](https://img.shields.io/github/last-commit/liangleionline/MobilePrint)](https://github.com/liangleionline/MobilePrint/commits/main)
[![Stars](https://img.shields.io/github/stars/liangleionline/MobilePrint)](https://github.com/liangleionline/MobilePrint/stargazers)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Android](https://img.shields.io/badge/Android-API%2026%2B-3DDC84?logo=android)](https://developer.android.com/)
[![Windows](https://img.shields.io/badge/platform-Windows%20x64-0078D6?logo=windows)](https://github.com/liangleionline/MobilePrint/releases/latest)

安卓手机通过局域网上传文件到 Windows 电脑，调用 USB 连接的 HP LaserJet 1020 打印。

**下载**：到 [Releases](https://github.com/liangleionline/MobilePrint/releases/latest) 取最新版本，每版同时提供 Windows 后端 `.zip` 与安卓 `.apk`。

## 架构

- **Windows 后端**：.NET 8 WPF 托盘程序，内置 Kestrel HTTP 服务（默认端口 **5003**），MS Office COM 转 PDF，Pdfium 渲染成整页位图，`System.Drawing.Printing` 提交打印。
- **Android 前端**：Kotlin + AppCompat（minSdk 26 / targetSdk 34），文件多选、图片本地压缩、上传进度、打印参数配置与本地持久化。

## 功能

- 支持格式：图片（JPG/JPEG/PNG/BMP/WEBP/GIF）、PDF、Office（DOC/DOCX/XLS/XLSX/PPT/PPTX，需本机装 MS Office）
- 一次多选多个文件，逐个上传、串行排队打印，末尾汇总成功/失败
- 打印参数：份数、纵向/横向、页码范围（如 `1,3,5-7`）、边距（mm）、缩放（适合A4 / 原始 / 自定义）、图片压缩开关
- 参数本地持久化：首次为默认值，之后记住上次关闭时的取值
- 安卓端图片预压缩（长边 1920px，质量 75）
- HTTPS 复选框：勾选后按 https 访问（配合反向代理，如 `https://你的域名:2443` → `http://内网IP:5003`）
- 任务队列管理：托盘右键「查看队列」，分组排序、右键取消/置顶/重试/批量删除
- 托盘右键「打印对齐测试页」：纸面边框 + 正中十字线 + 四边 10mm 刻度尺，可用尺子直接校验打印位置
- 文件自动归档到 `我的文档\MobilePrintFiles\`，日志在 `…\MobilePrintFiles\logs\`
- Token 鉴权：前后端写死同一个 Token（默认 `123456`），界面不显示、无需输入
- UDP 局域网设备发现

## 打印位置与「0 边距」

物理打印机存在**不可打印的硬边距**（HP LaserJet 1020 实测左/上各 3.81mm），所以物理上做不到真正的 0 边距。v1.1.6 修正了一个坐标原点问题：`PrintDocument` 默认 `OriginAtMargins = false` 时，`e.Graphics` 的原点在**可打印区**左上角而非纸张左上角，按纸张坐标直接绘制会让整幅内容向右下偏移一个硬边距。现在绘制前会先按 `HardMarginX/HardMarginY` 把原点搬回纸张左上角，0 边距模式下内容位置与源 PDF 一致。

## 版本历史

| 版本 | 日期 | 主要变更 |
| --- | --- | --- |
| v1.1.6 | 2026-11-09 | 修复内容偏下偏右（补偿打印机物理硬边距）；新增「打印对齐测试页」与打印诊断日志；托盘点位固定显示版本号 |
| v1.1.5 | 2026-11-06 | 文件选择改为多选，多个文件排队打印并汇总结果；发布产物移出版本库 |
| v1.1.4 | 2026-11-03 | 打印参数本地持久化（份数/边距/页码范围/方向/缩放/压缩） |
| v1.1.3 | 2026-11-03 | 新增 HTTPS 复选框；Token 写死在前后端，界面取消 Token 输入框 |
| v1.1.2 | 2026-11-02 | 清理冗余代码；去掉安卓端「查看日志」按钮；改为文件夹安装（放弃单文件 EXE），托盘图标正常显示 |
| v1.1.1 | 2026-11-02 | 托盘图标改为从安装目录读取；版本号写入程序与托盘提示 |
| v1.1.0 | 2026-11-01 | 托盘「端口设置」、安卓「测试连接」；日志查看；端口 80 → 8899 后统一为 5003；应用图标；开机自启 |
| v1.0.0 | 2026-11-01 | 初版：安卓上传 → Windows 后端 → HP LaserJet 1020 打印 |

> 每个版本在 Releases 页面均可下载；按约定每次发版只保留最近 3 个版本。

## 部署（Windows 后端）

1. 解压 Release 里的 `MobilePrint-vX.Y.Z-win-x64.zip` 到固定目录（如 `C:\Users\<你>\MobilePrint\`），**不要只留单个 exe**（托盘图标依赖安装目录结构）
2. 编辑 `appconfig.json`：`ListenPort`、`AuthToken`、`PrinterName`
3. 运行 `MobilePrint.Server.exe`，任务栏出现托盘图标
4. 右键托盘图标：开启 / 停止 / 重启服务、查看队列、端口设置、打印对齐测试页、退出
5. 开机自启（可选，当前需手动配置）：在注册表
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 下新建字符串值 `MobilePrint`，
   数据为 `"C:\Users\<你>\MobilePrint\MobilePrint.Server.exe"`（路径含空格时要带引号）

## 使用（Android 前端）

- 地址栏填电脑 IP + 端口（如 `192.168.0.197:5003`）；走反向代理时勾选 HTTPS 并填域名与端口
- Token 已内置，无需输入
- 选文件（可多选）→ 配置参数 → 打印；连接失败时下方打印按钮置灰

## 目录结构

```
MobilePrint.Server/      Windows 后端源码（WPF 托盘 + HTTP 服务 + 打印管线）
MobilePrint.Android/     Android 前端源码（Kotlin）
```

发布产物（`publish/`、`*.zip`、`*.apk`）不入库，见 `.gitignore`。

## 徽章说明

- `release` / `latest downloads` / `total downloads`：shields.io 实时读取 GitHub Releases 数据；下载量指 Release 资产的实际下载次数（不含 git clone），删除旧 Release 会使对应计数一并消失。
- `Visitors`：GitHub 不对外提供仓库访问量接口（Views 只有仓库所有者可在 Insights → Traffic 查看，14 天窗口），此徽章来自第三方计数服务，仅统计 README 渲染次数，作趋势参考。

## 文档维护约定

每次发布新版本时同步更新：本 README 的「版本历史」、该版本 Release 说明、仓库简介（About）。
