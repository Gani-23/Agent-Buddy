using ReactiveUI;

namespace AgentBuddy.ViewModels;

public class ChangePasswordViewModel : ViewModelBase
{
    private string _agentId = string.Empty;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _validationMessage = string.Empty;

    public ChangePasswordViewModel(string agentId)
    {
        AgentId = agentId ?? string.Empty;
    }

    public string AgentId
    {
        get => _agentId;
        set => this.RaiseAndSetIfChanged(ref _agentId, value);
    }

    public string CurrentPassword
    {
        get => _currentPassword;
        set => this.RaiseAndSetIfChanged(ref _currentPassword, value);
    }

    public string NewPassword
    {
        get => _newPassword;
        set => this.RaiseAndSetIfChanged(ref _newPassword, value);
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => this.RaiseAndSetIfChanged(ref _confirmPassword, value);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        set => this.RaiseAndSetIfChanged(ref _validationMessage, value);
    }

    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);

    public bool Validate()
    {
        if (string.IsNullOrWhiteSpace(AgentId))
        {
            ValidationMessage = "Agent ID is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(CurrentPassword))
        {
            ValidationMessage = "Enter the current password.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            ValidationMessage = "Enter a new password.";
            return false;
        }

        if (NewPassword != ConfirmPassword)
        {
            ValidationMessage = "New password and confirmation do not match.";
            return false;
        }

        ValidationMessage = string.Empty;
        return true;
    }
}
