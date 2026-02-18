using ReactiveUI;

namespace AgentBuddy.ViewModels;

public sealed class AslaasPromptRequest
{
    public string AccountNo { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string SuggestedAslaasNo { get; set; } = "APPLIED";
}

public sealed class AslaasPromptViewModel : ViewModelBase
{
    private string _aslaasNo;

    public AslaasPromptViewModel(AslaasPromptRequest request)
    {
        AccountNo = request.AccountNo;
        AccountName = request.AccountName;
        _aslaasNo = string.IsNullOrWhiteSpace(request.SuggestedAslaasNo)
            ? "APPLIED"
            : request.SuggestedAslaasNo;
    }

    public string AccountNo { get; }

    public string AccountName { get; }

    public string AslaasNo
    {
        get => _aslaasNo;
        set => this.RaiseAndSetIfChanged(ref _aslaasNo, value);
    }
}
