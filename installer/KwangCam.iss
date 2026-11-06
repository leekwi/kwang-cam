; 광쌤캠 설치 프로그램 (Inno Setup 6)
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist"
#endif

[Setup]
AppId={{3C9A6F21-7B4E-4D2A-9E61-5A8C2F0B7D14}
AppName=광쌤캠
AppVersion={#AppVersion}
AppVerName=광쌤캠 {#AppVersion}
AppPublisher=광쌤
DefaultDirName={autopf}\KwangCam
DefaultGroupName=광쌤캠
DisableProgramGroupPage=yes
OutputDir=..\out
OutputBaseFilename=KwangCam-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0
UninstallDisplayName=광쌤캠
UninstallDisplayIcon={app}\KwangCam.exe
CloseApplications=yes

[Languages]
#ifdef KOREAN
Name: "korean"; MessagesFile: "Korean.isl"
#else
Name: "english"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로가기 만들기"; Flags: unchecked

[Files]
Source: "{#SourceDir}\KwangCam.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\softcam.dll"; DestDir: "{app}"; Flags: ignoreversion restartreplace uninsrestartdelete
Source: "{#SourceDir}\x86\softcam.dll"; DestDir: "{app}\x86"; Flags: ignoreversion restartreplace uninsrestartdelete 32bit

[Icons]
Name: "{autoprograms}\광쌤캠"; Filename: "{app}\KwangCam.exe"
Name: "{autodesktop}\광쌤캠"; Filename: "{app}\KwangCam.exe"; Tasks: desktopicon

[Run]
; 가상 카메라 등록 (64비트 앱용 + 32비트 앱용)
Filename: "{sys}\regsvr32.exe"; Parameters: "/s ""{app}\softcam.dll"""; Flags: runhidden waituntilterminated; StatusMsg: "가상 카메라를 등록하는 중..."
Filename: "{syswow64}\regsvr32.exe"; Parameters: "/s ""{app}\x86\softcam.dll"""; Flags: runhidden waituntilterminated
; 폰이 PC로 접속할 수 있도록 방화벽 허용
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""KwangCam"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""KwangCam"" dir=in action=allow program=""{app}\KwangCam.exe"" enable=yes profile=any protocol=TCP"; Flags: runhidden waituntilterminated; StatusMsg: "방화벽을 설정하는 중..."
Filename: "{app}\KwangCam.exe"; Description: "광쌤캠 실행하기"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\regsvr32.exe"; Parameters: "/u /s ""{app}\softcam.dll"""; Flags: runhidden waituntilterminated; RunOnceId: "UnregCam64"
Filename: "{syswow64}\regsvr32.exe"; Parameters: "/u /s ""{app}\x86\softcam.dll"""; Flags: runhidden waituntilterminated; RunOnceId: "UnregCam32"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""KwangCam"""; Flags: runhidden waituntilterminated; RunOnceId: "DelFirewall"
