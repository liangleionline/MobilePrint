# MobilePrint — 手机远程打印系统

安卓手机通过局域网上传文件到 Windows 电脑，调用 USB 连接的 HP LaserJet 1020 打印机执行打印。

## 架构
- **Windows 后端**：.NET 8 WPF 托盘程序，内置 Kestrel HTTP 服务（端口80），Office COM 转PDF，Pdfium 渲染，System.Drawing.Printing 提交打印
- **Android 前端**：Kotlin，文件选择、图片本地压缩、上传、打印参数配置

## 功能
- 支持图片（JPG/PNG/BMP/WEBP）、PDF、Office（DOC/DOCX/XLS/XLSX/PPT/PPTX）
- 安卓端图片预压缩（长边1920px, 质量75）
- 打印参数：份数、纵向/横向、页码范围（如 1,3,5-7）、边距(mm)、缩放（适合A4/原始/自定义）
- 打印预览接口
- 任务队列管理：Windows 托盘右键「查看队列」弹窗，分组排序、右键取消/置顶/重试/批量删除
- 文件自动归档到 `我的文档\MobilePrintFiles\`
- Token 鉴权、UDP 局域网设备发现

## Windows 后端使用
1. 解压 `publish/win-x64/` 到任意目录
2. 编辑 `appconfig.json`：端口、Token、打印机名称
3. 运行 `MobilePrint.Server.exe`，任务栏出现托盘图标
4. 右键托盘图标：开启/停止/重启服务、查看队列、退出

## Android 前端
- 用 Android Studio 打开 `MobilePrint.Android/` 目录，编译生成 APK
- 打开 APP 输入电脑 IP 和 Token，选择文件后点打印

## 目录结构
```
MobilePrint.Server/      Windows 后端源码
MobilePrint.Android/    Android 前端源码
publish/win-x64/         Windows 后端已编译发布文件
```
