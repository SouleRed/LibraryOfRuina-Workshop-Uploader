# Library of Ruina Workshop Uploader

面向《废墟图书馆》Steam 创意工坊的轻量 WPF 上传工具，支持上传新模组、更新已有模组以及简体中文、English、日本語和 한국어即时切换。

## 主要功能

- 通过 Workshop ID 或完整工坊链接更新模组。
- 从 Steam 读取已有模组的标题、标签、描述和可见性。
- 支持选择、粘贴或拖放模组目录与预览图。
- 支持可见性、更新日志、上传进度和运行日志。
- 自动检测 .NET Framework 4.8，并记录启动错误。

## 使用

1. 启动并登录 Steam 客户端。
2. 运行 `SteamworkUploader.exe`。
3. 选择或拖入模组根目录。
4. 检查模组信息，选择“上传模组”或“更新模组”。
5. 设置可见性和更新日志后开始上传。

程序从模组根目录的 `StageModInfo.xml` 读取信息：

```xml
<Workshop>
  <Title>Example title</Title>
  <Description>Workshop description</Description>
  <Tag>Mod|Invitation</Tag>
  <PreviewImage>preview.jpg</PreviewImage>
</Workshop>
```

`PreviewImage` 为空或文件不存在时，会回退读取根目录的 `preview.jpg`。

## 编译

需要 Windows x64、.NET 8 SDK 或更新版本，以及 .NET Framework 4.8。

```powershell
dotnet build .\src\SteamworkUploader.csproj -c Debug
```

自然编译结果位于 `src\bin\Debug`。Steamworks 依赖固定存放在 `src\Dependencies`，不会在编译时下载 Facepunch.Steamworks 包。

## 开源说明

项目依据 [MIT License](LICENSE) 开源，并基于 [Flestal/SteamworkUploader](https://github.com/Flestal/SteamworkUploader) 的上传思路重新实现。第三方组件与致谢信息见 [NOTICE.md](NOTICE.md)。

本项目与 Project Moon、Valve 及原项目作者无官方关联。
