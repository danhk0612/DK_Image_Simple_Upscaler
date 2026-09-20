#include <windows.h>
#include <shellapi.h>
#include <filesystem>
#include <string>
#include <vector>

namespace
{
constexpr wchar_t kAppFileName[] = L"DKImageSimpleUpscaler.App.exe";
constexpr wchar_t kRuntimeDownloadUrl[] = L"https://dotnet.microsoft.com/en-us/download/dotnet/8.0";

bool HasDesktopRuntime8InRegistry()
{
    HKEY key = nullptr;
    const wchar_t* keyPath =
        L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64\\sharedfx\\Microsoft.WindowsDesktop.App";

    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, keyPath, 0, KEY_READ | KEY_WOW64_64KEY, &key) != ERROR_SUCCESS)
        return false;

    bool found = false;
    for (DWORD index = 0;; ++index)
    {
        wchar_t valueName[256]{};
        DWORD valueNameLength = static_cast<DWORD>(std::size(valueName));
        const LONG result = RegEnumValueW(
            key, index, valueName, &valueNameLength, nullptr, nullptr, nullptr, nullptr);

        if (result == ERROR_NO_MORE_ITEMS)
            break;

        if (result == ERROR_SUCCESS &&
            valueNameLength >= 2 &&
            valueName[0] == L'8' &&
            valueName[1] == L'.')
        {
            found = true;
            break;
        }
    }

    RegCloseKey(key);
    return found;
}

bool HasDesktopRuntime8InDefaultLocation()
{
    wchar_t programFiles[MAX_PATH]{};
    const DWORD length = GetEnvironmentVariableW(
        L"ProgramW6432", programFiles, static_cast<DWORD>(std::size(programFiles)));

    if (length == 0 || length >= std::size(programFiles))
        return false;

    const std::filesystem::path runtimeRoot =
        std::filesystem::path(programFiles) / L"dotnet" / L"shared" / L"Microsoft.WindowsDesktop.App";

    std::error_code error;
    if (!std::filesystem::is_directory(runtimeRoot, error))
        return false;

    for (const auto& entry : std::filesystem::directory_iterator(runtimeRoot, error))
    {
        if (error)
            return false;

        if (!entry.is_directory(error))
            continue;

        const std::wstring version = entry.path().filename().wstring();
        if (version.rfind(L"8.", 0) == 0)
            return true;
    }

    return false;
}

bool HasRequiredRuntime()
{
    return HasDesktopRuntime8InRegistry() || HasDesktopRuntime8InDefaultLocation();
}

std::filesystem::path GetLauncherDirectory()
{
    std::vector<wchar_t> buffer(32768);
    const DWORD length = GetModuleFileNameW(
        nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));

    if (length == 0 || length >= buffer.size())
        return {};

    return std::filesystem::path(std::wstring(buffer.data(), length)).parent_path();
}

void ShowRuntimeHelp()
{
    const int choice = MessageBoxW(
        nullptr,
        L"이 프로그램을 실행하려면 Microsoft .NET 8 Desktop Runtime (x64)이 필요합니다.\n\n"
        L"[예]를 누르면 Microsoft 공식 다운로드 페이지를 엽니다.\n"
        L"[아니요]를 누르면 종료합니다.",
        L"필요한 구성 요소가 없습니다",
        MB_YESNO | MB_ICONINFORMATION | MB_SETFOREGROUND);

    if (choice == IDYES)
    {
        ShellExecuteW(
            nullptr, L"open", kRuntimeDownloadUrl, nullptr, nullptr, SW_SHOWNORMAL);
    }
}

void ShowLaunchError(const std::wstring& message)
{
    MessageBoxW(
        nullptr,
        message.c_str(),
        L"DK Image Simple Upscaler 시작 오류",
        MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
}
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    if (!HasRequiredRuntime())
    {
        ShowRuntimeHelp();
        return 0;
    }

    const std::filesystem::path directory = GetLauncherDirectory();
    if (directory.empty())
    {
        ShowLaunchError(L"프로그램 실행 위치를 확인하지 못했습니다.");
        return 1;
    }

    const std::filesystem::path appPath = directory / kAppFileName;
    if (!std::filesystem::exists(appPath))
    {
        ShowLaunchError(
            L"필요한 프로그램 파일을 찾을 수 없습니다.\n\n"
            L"DKImageSimpleUpscaler.App.exe가 런처와 같은 폴더에 있는지 확인하세요.");
        return 1;
    }

    std::wstring commandLine = L"\"" + appPath.wstring() + L"\"";
    std::vector<wchar_t> commandBuffer(commandLine.begin(), commandLine.end());
    commandBuffer.push_back(L'\0');

    STARTUPINFOW startupInfo{};
    startupInfo.cb = sizeof(startupInfo);
    PROCESS_INFORMATION processInfo{};

    const std::wstring workingDirectory = directory.wstring();
    const BOOL started = CreateProcessW(
        appPath.c_str(),
        commandBuffer.data(),
        nullptr,
        nullptr,
        FALSE,
        0,
        nullptr,
        workingDirectory.c_str(),
        &startupInfo,
        &processInfo);

    if (!started)
    {
        ShowLaunchError(
            L"실제 프로그램을 시작하지 못했습니다. 파일이 손상되었거나 실행이 차단되었을 수 있습니다.");
        return 1;
    }

    CloseHandle(processInfo.hThread);
    CloseHandle(processInfo.hProcess);
    return 0;
}
