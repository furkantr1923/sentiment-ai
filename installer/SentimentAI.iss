; SENTIMENT AI — Windows kurulum betiği (Inno Setup 6)
; Derleme: iscc /DAppVersion=1.0.0 /DSourceDir=<yayın klasörü> /DOutputDir=<çıktı klasörü> installer\SentimentAI.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\out\win"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "SENTIMENT AI"
#define AppExe "SentimentAI.exe"

[Setup]
AppId={{6B0E7F3C-9A41-4C55-8E2B-5F1D2A7C9E31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Furkan
AppComments=Türkçe Metinlerin Duygu ve Anlam Analizi
DefaultDirName={autopf}\SENTIMENT AI
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Yönetici izni gerekmeden kullanıcıya özel kurulum (isteğe bağlı tüm kullanıcılar)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=SENTIMENT-AI-Setup
SetupIconFile=appicon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no
CloseApplications=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany=Furkan
VersionInfoDescription={#AppName} Kurulum

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Uygulamanın kendi önbelleği (geçici dosyalar). API anahtarları işletim sisteminin güvenli deposundadır.
Type: filesandordirs; Name: "{localappdata}\com.furkan.sentimentai"
