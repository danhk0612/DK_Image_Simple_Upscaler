# DK Image Simple Upscaler

글자가 포함된 UI/스크린샷은 원형을 최대한 보존하고, 사진/일러스트에는 선택적으로 AI 복원을 적용할 수 있는 Windows용 간단한 이미지 업스케일러입니다.

## 처리 모드

- **Text/UI Safe**: Lanczos 3 / Bicubic / Nearest Neighbor. 생성형 복원을 하지 않아 작은 글자와 UI 선 보존에 적합합니다.
- **AI General**: Real-ESRGAN `realesrgan-x4plus`를 사용합니다. 사진과 일반 이미지용입니다.
- **AI Anime**: Real-ESRGAN `realesrgan-x4plus-anime`를 사용합니다. 2D 일러스트/애니메이션 이미지용입니다.

AI 모드는 내부적으로 4× AI 복원 후 목표 크기에 맞춰 Lanczos로 정리합니다. `AI 강도`를 100보다 낮추면 같은 크기의 Text/UI Safe 결과와 AI 결과를 혼합해 과도한 가짜 디테일을 줄일 수 있습니다.

## 기능

- 이미지 파일 열기 및 드래그앤드롭
- 2× / 3× / 4× 확대
- FHD / QHD / 4K 안에 원본 비율을 유지해 맞춤
- Text/UI Safe: Lanczos 3 / Bicubic / Nearest Neighbor
- AI General / AI Anime
- AI 강도 0~100
- Real-ESRGAN Tile: Auto / 128 / 256 / 512
- 샤픈 0~100
- 원본/결과 나란히 미리보기
- PNG 또는 JPEG(품질 95) 저장
- AI 엔진 자동 설치

## 다운로드 및 실행

GitHub Releases의 `DKImageSimpleUpscaler-win-x64.zip`을 내려받아 원하는 폴더에 압축을 풀고 다음 파일을 실행합니다.

```text
DKImageSimpleUpscaler.exe
```

배포 폴더에는 두 실행 파일이 함께 있어야 합니다.

```text
DKImageSimpleUpscaler.exe       ← .NET 런타임 확인용 네이티브 런처
DKImageSimpleUpscaler.App.exe   ← 실제 업스케일러
```

실제 앱은 **Microsoft .NET 8 Desktop Runtime (x64)**을 사용하는 Framework-dependent Single-file 방식입니다. 런타임이 설치되어 있지 않으면 런처가 한국어 안내를 표시하고 Microsoft 공식 .NET 8 다운로드 페이지를 열 수 있습니다. 런타임 자체는 배포 ZIP에 포함하지 않습니다.

## 권장 설정

### UI, 문서 캡처, 한글/영문 글자가 많은 이미지

- 모드: `Text/UI Safe`
- 방식: `Lanczos 3`
- 샤픈: `10~25`
- 저장: PNG

### 사진

- 모드: `AI General`
- AI 강도: `70~90`
- 샤픈: `0~10`
- Tile: `Auto`, VRAM 부족 시 256 또는 128

### 2D 일러스트 / 애니메이션 이미지

- 모드: `AI Anime`
- AI 강도: `80~100`
- 샤픈: `0~10`

글자가 포함된 이미지에서 AI가 글자 획을 바꾸면 AI 강도를 낮추거나 Text/UI Safe를 사용하십시오.

## AI 엔진

AI 기능은 공식 [Real-ESRGAN](https://github.com/xinntao/Real-ESRGAN)의 Windows NCNN/Vulkan portable 패키지를 필요할 때 다운로드합니다.

- 고정 패키지: Real-ESRGAN `v0.2.5.0` Windows NCNN/Vulkan
- 설치 위치: `%LOCALAPPDATA%\DKImageSimpleUpscaler\AI\RealESRGAN\v0.2.5.0`
- 프로그램 저장소와 배포 ZIP에는 AI 실행 파일이나 모델을 포함하지 않습니다.
- Real-ESRGAN / Real-ESRGAN-ncnn-vulkan의 각 라이선스는 원 프로젝트를 따릅니다.

## 빌드

전체 배포물을 로컬에서 만들려면 다음이 필요합니다.

- .NET 8 SDK
- Visual Studio 2022 C++ Build Tools (MSVC x64)

PowerShell에서:

```powershell
.\build.ps1
```

생성 결과:

```text
dist\
├─ DKImageSimpleUpscaler.exe
└─ DKImageSimpleUpscaler.App.exe
```

GitHub Actions의 릴리스 워크플로도 같은 `build.ps1`을 사용하므로 로컬 빌드와 릴리스 패키지 구조가 동일합니다.
