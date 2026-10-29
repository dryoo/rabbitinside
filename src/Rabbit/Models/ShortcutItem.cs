using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace Rabbit.Models;

/// <summary>
/// 런처에 등록된 바로가기 하나를 나타냅니다.
/// 3단계에서 쓸 정렬 기준(최근 실행, 실행 빈도)과 고정핀 필드를 미리 담아둡니다.
/// </summary>
public sealed class ShortcutItem : INotifyPropertyChanged
{
    private BitmapSource? _icon;
    private string _displayName = string.Empty;
    private bool _isPinned;
    private bool _isMissing;
    private bool _isShortcutDetached;
    private long? _sizeBytes;

    /// <summary>바로가기 파일의 전체 경로입니다(.lnk 또는 .url).</summary>
    public required string Path { get; init; }

    /// <summary>화면에 표시할 이름입니다. 기본값은 확장자를 뺀 파일명입니다.</summary>
    public required string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset? LastLaunchedAt { get; set; }

    public int LaunchCount { get; set; }

    /// <summary>고정한 항목은 정렬 기준과 무관하게 항상 위에 옵니다.</summary>
    public bool IsPinned
    {
        get => _isPinned;
        set => SetField(ref _isPinned, value);
    }

    /// <summary>설치 용량(바이트)입니다. 아직 계산하지 않았으면 null입니다.</summary>
    public long? SizeBytes
    {
        get => _sizeBytes;
        set
        {
            if (SetField(ref _sizeBytes, value))
            {
                RaisePropertyChanged(nameof(SizeText));
            }
        }
    }

    public DateTimeOffset? SizeComputedAt { get; set; }

    /// <summary>용량을 마지막으로 확인해 본 시각입니다. 실패한 항목을 계속 다시 훑지 않도록 남깁니다.</summary>
    public DateTimeOffset? SizeAttemptedAt { get; set; }

    /// <summary>파일이 아니라 셸 항목(스토어 앱 등)을 가리키면 true입니다.</summary>
    public bool IsShellTarget { get; init; }

    /// <summary>사용자가 직접 지정한 아이콘 파일입니다. 지정하면 자동 선택보다 우선합니다.</summary>
    public string? IconOverride { get; set; }

    /// <summary>
    /// 바로가기가 사라졌을 때 대신 실행할 대상입니다.
    /// 아이콘·용량을 확인할 때 알게 된 값을 기억해 둡니다. 바로가기가 살아 있으면 쓰이지 않습니다.
    /// </summary>
    public string? FallbackTarget { get; set; }

    public string? FallbackArguments { get; set; }

    public string? FallbackWorkingDirectory { get; set; }

    /// <summary>아이콘을 다시 뽑을 때 쓸 원본 경로입니다(대상 실행 파일이나 아이콘 파일).</summary>
    public string? FallbackIcon { get; set; }

    /// <summary>steam:// 같은 주소로 실행하는 항목의 대체 대상입니다.</summary>
    public string? FallbackUrl { get; set; }

    /// <summary>바로가기나 그 대상이 사라졌으면 true입니다. 저장하지 않고 실행할 때마다 다시 확인합니다.</summary>
    [JsonIgnore]
    public bool IsMissing
    {
        get => _isMissing;
        set
        {
            if (SetField(ref _isMissing, value))
            {
                RaisePropertyChanged(nameof(NeedsAttention));
                RaisePropertyChanged(nameof(MissingText));
            }
        }
    }

    /// <summary>바로가기는 사라졌지만 기억해 둔 대상으로 실행할 수 있는 상태입니다.</summary>
    [JsonIgnore]
    public bool IsShortcutDetached
    {
        get => _isShortcutDetached;
        set
        {
            if (SetField(ref _isShortcutDetached, value))
            {
                RaisePropertyChanged(nameof(NeedsAttention));
                RaisePropertyChanged(nameof(MissingText));
            }
        }
    }

    /// <summary>표시가 필요한 상태인지(실행할 수 없거나, 바로가기 없이 실행되는 상태).</summary>
    [JsonIgnore]
    public bool NeedsAttention => _isMissing || _isShortcutDetached;

    /// <summary>툴팁에 넣을 안내 문구입니다. 멀쩡하면 빈 문자열입니다.</summary>
    [JsonIgnore]
    public string MissingText => _isMissing
        ? "파일을 찾을 수 없습니다"
        : _isShortcutDetached
            ? "바로가기 대신 실행 파일로 실행합니다"
            : string.Empty;

    /// <summary>타일 아래에 보여줄 용량 문자열입니다. 계산 전이면 비어 있습니다.</summary>
    [JsonIgnore]
    public string SizeText => _sizeBytes is { } bytes ? FormatSize(bytes) : string.Empty;

    /// <summary>셸에서 뽑아낸 아이콘입니다. 저장하지 않고 실행할 때마다 다시 읽습니다.</summary>
    [JsonIgnore]
    public BitmapSource? Icon
    {
        get => _icon;
        set => SetField(ref _icon, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public static ShortcutItem FromFile(string shortcutPath) =>
        FromTarget(shortcutPath, displayName: null, isShellTarget: false);

    public static ShortcutItem FromTarget(string target, string? displayName, bool isShellTarget) => new()
    {
        Path = target,
        DisplayName = string.IsNullOrWhiteSpace(displayName)
            ? System.IO.Path.GetFileNameWithoutExtension(target)
            : displayName,
        IsShellTarget = isShellTarget,
    };

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }

    private void RaisePropertyChanged(string? propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit <= 1
            ? $"{value:0} {units[unit]}"
            : $"{value:0.#} {units[unit]}";
    }
}
