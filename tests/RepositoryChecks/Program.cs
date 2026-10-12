using AneedWeb.Data;
using AneedWeb.Services;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
var repository = new LibraryRepository(builder.Configuration, builder.Environment);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
Check(repository.Configured, "Supply ConnectionStrings:Library for PostgreSQL test.");

var songs = await repository.Songs();
Check(songs.Count > 0, "Expected synced tracks in music.catalog_tracks.");

var first = songs.First();
Check(first.Id > 0, "SAM ID must be positive.");
Check((await repository.Songs(id: first.Id)).Any(s => s.Id == first.Id),
    "Song lookup uses SAM song ID.");
Check((await repository.Songs(id: long.MaxValue)).Count == 0,
    "Unknown SAM ID returns no songs.");

var artists = await repository.Artists();
Check(artists.Count > 0, "Expected artists populated by migration.");
var artist = artists.First();
Check((await repository.Artists(id: artist.Id)).Single().Id == artist.Id,
    "Artist lookup uses stable PostgreSQL artist ID.");
var artistSongs = await repository.Songs(artist: artist.Id);
Check(artistSongs.Count > 0 && artistSongs.All(s => s.ArtistId == artist.Id),
    "Tracks resolve to the stable artist ID.");

Check((await repository.Songs("' OR 1=1 --")).Count == 0,
    "Search input must not be interpreted as SQL.");
var broadcast = await repository.BroadcastSongs([new Track(first.Title, first.Artist, null, null)]);
Check(broadcast.Any(s => s.Id == first.Id), "Live365 lookup resolves a catalog track.");

Console.WriteLine("PASS: PostgreSQL catalog reads, stable artist IDs, safe search, song details, broadcast matching.");
