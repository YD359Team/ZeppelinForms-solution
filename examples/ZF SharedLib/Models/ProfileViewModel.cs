using System.ComponentModel;
using System.Runtime.CompilerServices;
using ZeppelinForms.Input.Commands;

namespace ZF_SharedLib.Models;

/// <summary>
/// A view model as MVVM writes one: properties that say when they change, a command, and no
/// knowledge of any control. ProfileCard.zfml binds to it through its data context.
/// </summary>
/// <remarks>
/// Written by hand to show what the bindings need — INotifyPropertyChanged and ICommand,
/// nothing of the framework's. CommunityToolkit.Mvvm's [ObservableProperty] and
/// [RelayCommand] write the same and work the same.
/// </remarks>
public sealed class ProfileViewModel : INotifyPropertyChanged
{
    private string _name = "Zeppelin";
    private string _email = string.Empty;
    private string _status = "Not saved yet";

    public ProfileViewModel()
    {
        Save = new Command(DoSave, () => CanSave);
        Address.PropertyChanged += (_, _) => Save.NotifyCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            if (!Set(ref _name, value)) return;

            // a property computed from another says it changed too
            OnPropertyChanged(nameof(Greeting));
            Save.NotifyCanExecuteChanged();
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            if (Set(ref _email, value)) Save.NotifyCanExecuteChanged();
        }
    }

    public string Greeting => string.IsNullOrWhiteSpace(Name) ? "Who are you?" : $"Hello, {Name}!";

    /// <summary>An object of its own: {Bind Address.City} follows it, and a new address
    /// set here would be followed instead.</summary>
    public AddressViewModel Address { get; } = new();

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public Command Save { get; }

    private bool CanSave => !string.IsNullOrWhiteSpace(Name) && Email.Contains('@') && Address.City.Length > 0;

    private void DoSave() => Status = $"Saved at {DateTime.Now:T}: {Name}, {Email}, {Address.City}";

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>The address of a profile: a view model inside a view model.</summary>
public sealed class AddressViewModel : INotifyPropertyChanged
{
    private string _city = "Friedrichshafen";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string City
    {
        get => _city;
        set
        {
            if (_city == value) return;

            _city = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(City)));
        }
    }
}
