# 광쌤캠 (KwangCam)

안드로이드 핸드폰 카메라를 Windows PC의 **웹캠처럼** 쓰게 해 주는 프로그램이에요.
Zoom, Teams, Google Meet(크롬) 같은 프로그램의 카메라 목록에 **"광쌤캠 (KwangCam)"**으로 나타나요.

## 다운로드

[Releases 페이지](../../releases/tag/latest)에서 받으세요.

| 파일 | 설치할 곳 |
|---|---|
| `KwangCam-Setup.exe` | Windows 10/11 PC |
| `KwangCam.apk` | 안드로이드 8.0 이상 핸드폰 |

## 사용 방법

1. PC에 `KwangCam-Setup.exe`를 설치하고 실행해요.
   - "Windows의 PC 보호" 창이 뜨면 **추가 정보 → 실행**을 누르세요. (코드 서명 인증서가 없어서 나오는 경고예요.)
2. 핸드폰에 `KwangCam.apk`를 설치하고 실행해요.
   - "출처를 알 수 없는 앱" 경고가 나오면 설치를 허용해 주세요.
3. 폰 앱에서 **[QR 연결]**을 누르고 PC 화면의 QR코드를 찍어요.
   - QR이 안 되면 **[주소 입력]**에 PC 화면에 적힌 주소를 넣어요.
4. Zoom → 설정 → 비디오 → 카메라에서 **광쌤캠 (KwangCam)**을 골라요.
   - 얼굴 웹캠은 그대로 두고 교재만 보여주려면: 화면 공유 → 고급 → **보조 카메라의 콘텐츠**

> PC와 폰은 **같은 와이파이**에 연결되어 있어야 해요.
> 다음부터는 폰 앱을 켜기만 하면 지난번 PC에 자동으로 다시 연결돼요.

## 기능

- 폰: 앞/뒤 카메라 전환, 손전등, 화면 눌러 초점 맞추기, 화질(보통/고화질), 화면 어둡게(배터리 절약)
- PC: 90° 회전, 좌우·상하 반전, 화면 꽉 채우기, 연결 안 됐을 때 안내 화면 표시
- 폰을 가로/세로 어느 쪽으로 세워도 영상이 바로 서요.

## 구조 (개발자용)

```
android/     안드로이드 앱 (Kotlin, CameraX) — 카메라 영상을 JPEG으로 압축해 TCP로 전송
windows/     PC 프로그램 (C# .NET 8, WinForms) — 영상을 받아 가상 카메라로 내보냄
installer/   설치 프로그램 (Inno Setup)
.github/     GitHub Actions — main에 올리면 APK와 설치 파일을 자동으로 빌드해 Releases에 올림
```

- 가상 카메라는 [softcam](https://github.com/tshino/softcam) (MIT 라이선스)을 빌드할 때 받아서, 이름과 ID만 광쌤캠용으로 바꿔 씁니다.
- 통신: 폰 → PC, TCP 51234번 포트. 프레임 형식은 `"KWC1" | 회전(uint16) | 플래그(uint8) | 예약(uint8) | 길이(uint32) | JPEG` (빅엔디언).

## 라이선스

- softcam: MIT License, Copyright (c) 2020 tshino
