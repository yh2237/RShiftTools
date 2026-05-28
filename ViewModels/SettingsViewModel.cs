using System.Collections.ObjectModel;
using System.Reflection;
using RShiftTools.Services;

namespace RShiftTools.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    public ObservableCollection<string> HwEncoders { get; } =
    ["自動 (CPU)", "NVIDIA (nvenc)", "AMD (amf)", "Intel (qsv)"];

    private string _hwEncoder = UserSettings.HwEncoder;
    public string HwEncoder
    {
        get => _hwEncoder;
        set
        {
            _hwEncoder = value;
            OnPropertyChanged();
            UserSettings.HwEncoder = value;
        }
    }

    public ObservableCollection<string> ContextMenuModes { get; } =
    ["表示しない", "現在のユーザーのみ", "すべてのユーザー"];

    private string _contextMenuMode = "表示しない";
    public string ContextMenuMode
    {
        get => _contextMenuMode;
        set
        {
            _contextMenuMode = value;
            OnPropertyChanged();
        }
    }

    public string Version
    {
        get
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            return ver is null ? "1.0.0" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }
    }

    public string AppTitle => $"{AppStrings.AppName} v{Version}";

    private string _installStatus = "";
    public string InstallStatus
    {
        get => _installStatus;
        set
        {
            _installStatus = value;
            OnPropertyChanged();
        }
    }

    public void ApplyContextMenu()
    {
        try
        {
            if (_contextMenuMode == "表示しない")
            {
                RegistryService.Unregister(allUsers: true);
                RegistryService.Unregister(allUsers: false);
                InstallStatus = "右クリックメニューの登録を削除しました。";
            }
            else
            {
                var allUsers = _contextMenuMode == "すべてのユーザー";
                RegistryService.Register(AppContext.BaseDirectory, allUsers: allUsers);
                InstallStatus = allUsers
                    ? "全ユーザーへ右クリックメニューを登録しました。"
                    : "現在のユーザーへ右クリックメニューを登録しました。";
            }
        }
        catch (UnauthorizedAccessException)
        {
            InstallStatus = "管理者権限が必要です。管理者として実行してください。";
        }
        catch (Exception ex)
        {
            InstallStatus = $"登録に失敗しました: {ex.Message}";
        }
    }
}
