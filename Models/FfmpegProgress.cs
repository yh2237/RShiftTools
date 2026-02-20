namespace RShiftTools.Models;

public class FfmpegProgress
{
    public double Percent { get; init; }
    public TimeSpan CurrentTime { get; init; }
    public string RawLine { get; init; } = "";
}
