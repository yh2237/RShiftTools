namespace RShiftTools.Services;

public interface IDialogService
{
    string? AskOutputPath(string inputPath, string outputExt);
    string? AskOutputFolder(string suggestedDir);
}
