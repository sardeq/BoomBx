using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using BoomBx.Models;
using CommunityToolkit.Mvvm.ComponentModel;

public partial class Soundboard : ObservableObject
{
    [JsonInclude]
    public ObservableCollection<SoundItem> Sounds { get; set; } = new();

    [ObservableProperty]
    [property: JsonInclude]
    private string? _name;
}