; Modbus 调试工具 Inno Setup 安装脚本
; 使用方法：
; 1. 先在项目根目录执行发布命令。
; 2. 使用 Inno Setup Compiler 打开本文件并编译。
; 3. 安装包输出到 artifacts\installer。

#define MyAppName "Modbus 调试工具"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ModbusHelper"
#define MyAppExeName "ModbusHelper.WinForms.exe"
#define PublishDir "..\artifacts\ModbusHelper-win-x64"

; 当前发布目录是依赖框架发布，目标电脑需要安装 .NET 8 Desktop Runtime。
; 若希望安装包自带运行时，请先使用 --self-contained true 发布，
; 再把 PublishDir 改为对应的自包含发布目录。

[Setup]
AppId={{7BEB6D4D-5A9E-4A3C-8C48-7E36EAFD9F35}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\ModbusHelper
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=ModbusHelper-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ChangesAssociations=no
CloseApplications=yes
RestartApplications=no
; 不把用户数据库加入安装包，避免发布开发机上的个人配置。
; 应用运行后生成的 ModbusHelper.db 不会被卸载程序删除。

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
; 发布目录应包含 EXE、依赖程序集和运行时所需文件。
; 数据库及 SQLite 临时文件不随安装包分发。
Source: "{#PublishDir}\*"; DestDir: "{app}"; \
    Excludes: "ModbusHelper.db,ModbusHelper.db-wal,ModbusHelper.db-shm"; \
    Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; \
    Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; \
    GroupDescription: "附加快捷方式："; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; \
    Flags: nowait postinstall skipifsilent
