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

安卓手机通过局域网上传文件到 Windows 电脑，调用 USB 连接的 HP LaserJet 1020 打印机打印。

**下载**：到 [Releases](https://github.com/liangleionline/MobilePrint/releases/latest) 取最新版本，每次发布都同时包含 Windows 后端 `.zip` 与安卓 `.apk`。

## 徽章说明

- `release` / `latest downloads` / `total downloads`：由 shields.io 实时读取 GitHub Releases 数据，下载量即 Release 资产的实际下载次数（不含 git clone）。
- `Visitors`：GitHub 不对外提供仓库访问量接口（Views 只有仓库所有者能在 Insights → Traffic 里看到），因此这里的访问计数来自第三方计数服务，仅统计 README 被渲染的次数，作趋势参考。

## 架构

- **Windows 后端**：.NET 8 WPF 托盘程序，内置 Kestrel HTTP 服务（默认端口 5003），MS Office COM 转 PDF，Pdfium 渲染，`System.Drawing.Printing` 提交打印。
- **Android 前端**：Kotlin，文件多选、图片本地压缩、上传进度、打印参数配置与本地持久化。

## 功能

- 支持图片（JPG/PNG/BMP/WEBP）、PDF、Office（DOC/DOCX/XLS/XLSX/PPT/PPTX）
- 一次多选多个文件，逐个上传、串行排队打印，末尾汇总成功/失败
- 打印参数：份数、纵向/横向、页码范围（如 `1,3,5-7`）、边距(mm)、缩放（适合A4/原始/自定义）、图片压缩开关
- 参数本地持久化：首次为默认值，之后记住上次关闭时的取值
- 安卓端图片预压缩（长边 1920px，质量 75）
- 支持 HTTPS 复选框（配合反向代理，如 `https://印服务器域名:端口` → `http://内网IP:5003`）
- 任务队列管理：托盘右键「查看队列」，分组排序、右键取消/置顶/重试/批量删除
- 托盘右键「打印对齐测试页」：纸面边框 + 正中十字线 + 四边 10mm 刻度尺，可用尺子直接校验打印位置
- 文件自动归档到 `我的文档\MobilePrintFiles\`
- Token 鉴权（写死在前后端，界面不再显示）、UDP 局域网设备发现
- 开机自启（注册表 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`）

## 打印位置与「0 边距」

物理打印机存在**不可打印的硬边距**，HP LaserJet 1020 实测为左/上各 3.81mm，所以物理上做不到真正的 0 边距。v1.1.6 修正了一个坐标原点问题：`PrintDocument` 默认 `OriginAtMargins = false` 时，`e.Graphics` 的原点在**可打印区**左上角而非纸张左上角，按纸张坐标直接绘制会让整幅内容向右下偏移一个硬边距。现在绘制前会先按 `HardMarginX/HardMarginY` 把原点搬回纸张左上角，0 边距模式下内容位置与源 PDF 一致。

## Windows 后端使用

1. 解压 Release 里的 `MobilePrint-vX.Y.Z-win-x64.zip` 到固定目录（如 `C:\Users\<你>\MobilePrint\`），不要只留单个 exe
2. 编辑 `appconfig.json`：`ListenPort`、`AuthToken`、`PrinterName`
3. 运行 `MobilePrint.Server.exe`，任务栏出现托盘图标
4. 右键托盘图标：开启/停止/重启服务、查看队列、端口设置、打印对齐测试页、退出

## Android 前端

- 安卓端地址栏填电脑 IP + 端口（如 `192.168.0.197:5003`），需要反代时勾选 HTTPS
- Token 已内置，无需输入
- 选文件（可多选）→ 配置参数 → 打印

## 目录结构

```
MobilePrint.Server/      Windows 后端源码
MobilePrint.Android/     Android 前端源码
```

发布产物由 CI/本地 `dotnet publish` 生成，不入库（见 `.gitignore`）。
