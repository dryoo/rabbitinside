namespace Rabbit.Services;

/// <summary>타일 한 칸의 배치 크기입니다.</summary>
public readonly record struct TileLayout(
    double IconSize,
    double ItemWidth,
    double ItemHeight,
    double TopPadding,
    bool Fits)
{
    /// <summary>칸 폭에서 아이콘과 여백을 뺀, 양옆에 남는 공간(칸 전체 기준)입니다.</summary>
    public double HorizontalGap => ItemWidth - (IconSize + 16);

    /// <summary>칸 높이에서 타일 높이를 뺀 세로 간격입니다.</summary>
    public double VerticalGap => ItemHeight - (IconSize + 8);
}

/// <summary>
/// 창 크기와 항목 수에 맞춰 타일 크기와 간격을 정합니다.
/// 오른쪽과 아래에 빈 공간이 남지 않도록 열 수를 맞추고, 남는 세로 공간은 줄 간격으로 나눠 넣습니다.
/// </summary>
public static class AutoTileSizer
{
    public const double MinIconSize = 44;
    public const double MaxIconSize = 224;

    /// <summary>아이콘 둘레에 들어가는 가로 여백(타일 여백 16 + 칸 여백 8)입니다.</summary>
    private const double SlotPadding = 24;

    /// <summary>타일 높이에서 아이콘 외에 차지하는 값(위아래 여백)입니다.</summary>
    private const double TileVerticalPadding = 16;

    /// <summary>줄 사이를 벌릴 때 허용하는 최대 추가 간격입니다.</summary>
    private const double MaxExtraRowGap = 28;

    private const double Step = 2;

    /// <summary>아이콘 원본이 256px이므로 고DPI에서는 그만큼 작게 잡아 확대하지 않습니다.</summary>
    private const double SourceIconSize = 256;

    public static TileLayout Compute(
        double availableWidth,
        double availableHeight,
        int itemCount,
        double dpiScale = 1)
    {
        if (availableWidth <= 0 || availableHeight <= 0 || itemCount <= 0)
        {
            return new TileLayout(64, 80, 80, 0, true);
        }

        var maxIcon = Math.Max(
            MinIconSize,
            Math.Min(MaxIconSize, SourceIconSize / Math.Max(1, dpiScale)));

        // 반올림 오차로 마지막 칸이 다음 줄로 넘어가지 않도록 1px 여유를 둡니다.
        var usableWidth = Math.Max(1, availableWidth - 1);
        var usableHeight = Math.Max(1, availableHeight - 1);

        // 항목 하나에 주어지는 넓이의 제곱근을 기준으로 삼습니다.
        var ideal = Math.Clamp(
            Math.Sqrt(usableWidth * usableHeight / itemCount) * 0.62,
            MinIconSize,
            maxIcon);

        var best = default(TileLayout);
        var bestScore = double.NegativeInfinity;
        var maxColumns = (int)Math.Min(itemCount, 24);

        for (var columns = 1; columns <= maxColumns; columns++)
        {
            var icon = Math.Clamp(
                Math.Floor((usableWidth / columns - SlotPadding) / Step) * Step,
                MinIconSize,
                maxIcon);

            var slotWidth = icon + SlotPadding;
            var usedWidth = columns * slotWidth;

            // 이 열 수로는 폭에 들어가지 않습니다.
            if (usedWidth > usableWidth)
            {
                continue;
            }

            var wasteWidth = usableWidth - usedWidth;
            var rows = (int)Math.Ceiling(itemCount / (double)columns);
            var slotHeight = icon + TileVerticalPadding;
            var fits = rows * slotHeight <= usableHeight;
            var wasteHeight = fits ? Math.Max(0, usableHeight - (rows * slotHeight)) : 0;

            // 아이콘이 큰 쪽, 오른쪽이 덜 남는 쪽, 스크롤이 생기지 않는 쪽을 고릅니다.
            var score = icon - (wasteWidth * 0.6) - (fits ? wasteHeight * 0.25 : 200);

            if (score <= bestScore)
            {
                continue;
            }

            // 세로로 남는 공간은 줄 간격으로 나눠 넣고, 그래도 남으면 위아래로 반씩 나눕니다.
            var itemHeight = fits
                ? Math.Min(usableHeight / rows, slotHeight + MaxExtraRowGap)
                : slotHeight;

            var topPadding = fits ? Math.Max(0, (usableHeight - (rows * itemHeight)) / 2) : 0;

            bestScore = score;
            best = new TileLayout(icon, usableWidth / columns, itemHeight, topPadding, fits);
        }

        return best.IconSize > 0
            ? best
            : new TileLayout(ideal, ideal + SlotPadding, ideal + TileVerticalPadding, 0, false);
    }
}
