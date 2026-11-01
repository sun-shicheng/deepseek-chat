# DeepSeek Chat · 本地客户端

一个无需安装、单文件运行的 DeepSeek 风格聊天客户端，面向**内网自建 / 代理转发**的 OpenAI 兼容接口。
用 Windows 自带的 .NET Framework 编译，不依赖 Electron、WebView2 或任何联网组件。

## 功能

- 聊天窗口：流式输出（打字机效果）、停止生成、Markdown 渲染（加粗 / 行内代码 / 代码块 / 列表）
- 可配置接口：接口地址、接口路径（可填完整 URL）、API Key、模型名
- HTTP 代理：`host:port` + 可选账号密码
- 配置记忆：接口地址、Key、模型、代理、主题等保存在本机，重开 exe 不用重新填写
- 明暗主题：深色 / 浅色一键切换（`Ctrl+T`），记忆上次选择
- 多会话：新建对话、历史会话切换、右键删除
- 内网适配：可忽略 HTTPS 证书错误（自签名证书）
- API Key 使用 Windows DPAPI（当前用户）加密后保存

## 运行

直接双击 `DeepSeekChat.exe`。第一次打开后：

1. 点左下角 **设置**（或按 `Ctrl+,`）
2. 填写：
   - **接口地址**：如 `https://api.deepseek.com` 或 `http://10.0.0.5:8000`
   - **接口路径**：默认 `/v1/chat/completions`；如果网关给的是完整地址，也可以直接把完整 URL 填到这里
   - **模型名称**：如 `deepseek-chat`、`qwen2.5-72b-instruct`，以服务端为准
   - **API Key**：内网网关的密钥（可留空，由网关自行鉴权时）
   - **HTTP 代理**：如 `127.0.0.1:7890`，不需要就留空
   - 自签名证书的内网 HTTPS，勾选 **忽略 HTTPS 证书错误**
3. 点 **测试连接**（或按 `F5`），成功后再点 **保存**

接口按 OpenAI 兼容协议调用：`POST {接口地址}{接口路径}`，请求头 `Authorization: Bearer {API Key}`。

## 快捷键

| 快捷键 | 功能 |
| --- | --- |
| `Enter` | 发送消息 |
| `Shift+Enter` | 输入换行 |
| `Ctrl+N` | 新建对话 |
| `Ctrl+T` | 切换明暗主题 |
| `Ctrl+,` | 打开设置 |
| `F5`（设置窗口内） | 测试连接 |

生成过程中按 `Enter` 或点 **停止** 可中断输出，已生成的内容会保留。

## 配置文件

保存在 `%APPDATA%\DeepSeekDesktop\`：

- `config.ini`：接口地址、模型、代理等；API Key / 代理密码以 DPAPI 加密（只能由当前 Windows 用户解密）
- `sessions.dat`：历史会话（正文 Base64 编码，避免换行与特殊字符问题）

删除该目录即可恢复出厂设置。

## 从源码编译

需要 Windows 自带的 .NET Framework 4.x（`csc.exe`），无需安装 SDK、无需联网：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

生成 `DeepSeekChat.exe`（约 60 KB）。修改图标后执行 `make-icon.ps1` 重新生成 `app.ico`。

## 已知限制

- 仅支持 OpenAI 兼容的 Chat Completions 协议；若内网网关使用其他协议，需要自行调整 `src/ChatClient.cs`
- 代理为 HTTP 代理（不支持 SOCKS）
- 窗口标题栏颜色跟随系统主题，不随应用内明暗主题变化
- 只保留最近 20 条消息（约 10 轮）作为上下文发送（避免超长请求）
