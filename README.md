# AI Proxy Server & Extension

这是一个旨在为旧系统（Legacy Web Systems）提供 AI 增强能力的工具集，由 **Hyperion** 构建。

## 项目组件

### 1. GeminiProxyServer (ASP.NET Core 10)
一个轻量级代理服务器，充当 AI（Gemini CLI）和前端应用之间的桥梁。

- **核心功能**:
  - 提供 `/api/parse` 接口：接收图像或 PDF 字节流以及自然语言指令。
  - 调用 `gemini` CLI 进行多模态解析。
  - 返回结构化的 JSON 数据，以便前端自动填充表单。
  - 提供 `/api/health` 监控接口。
- **运行端口**: 默认监听 `http://localhost:5001`。

### 2. AiExtension (Chrome Extension)
一个基于 Manifest V3 的 Chrome 浏览器扩展，用于在浏览器侧边栏中与 AI 交互并操作网页 DOM。

- **核心功能**:
  - **Side Panel**: 提供文件上传（发票、报表截图）和指令输入界面。
  - **Content Script**: 针对 ASP.NET WebForms 优化的 DOM 填充逻辑。
    - 支持通过 ID 后缀定位嵌套控件。
    - 支持 GridView 明细行模式匹配与填充。
    - 提供填充后的视觉闪烁反馈。
  - **Background Service**: 维护与代理服务器的通信。

## 快速开始

### 启动代理服务器
```bash
cd GeminiProxyServer
dotnet run
```

### 安装扩展
1. 打开 Chrome 浏览器，进入 `chrome://extensions/`。
2. 开启“开发者模式”。
3. 点击“加载已解压的扩展程序”，选择本项目中的 `AiExtension` 文件夹。

---
*Built with ❤️ by Hyperion*
