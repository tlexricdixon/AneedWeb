using AneedWeb.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.SqlClient;

namespace AneedWeb.Components.Pages;

public partial class Library
{
    [Inject]
    private LibraryRepository Repository { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "q")]
    public string? Q { get; set; }

    private string query = string.Empty;
    private List<Song> songs = [];
    private List<Artist> artists = [];
    private string? error;
    private bool busy;

    protected override async Task OnParametersSetAsync()
    {
        query = Q ?? string.Empty;
        await Load();
    }

    private void Search()
    {
        var searchTerm = Uri.EscapeDataString(query.Trim());
        Navigation.NavigateTo($"/library?q={searchTerm}");
    }

    private async Task Load()
    {
        busy = true;
        error = null;

        try
        {
            if (!Repository.Configured)
            {
                error = "The music library is getting connected. Please check back soon.";
                return;
            }

            songs = await Repository.Songs(query);
            artists = await Repository.Artists(query);
        }
        catch (SqlException)
        {
            error = "The library is temporarily unavailable. Please try again later.";
        }
        finally
        {
            busy = false;
        }
    }
}
