using AneedWeb.Data;
using AneedWeb.Services;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);
var repository = new LibraryRepository(builder.Configuration, builder.Environment);
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

var songs = await repository.Songs("queen");
Check(songs.Count == 2 && songs.All(s => s.Artist == "Queen"), "Artist search must include songs.");
var song = (await repository.Songs(id: 1)).Single();
Check(song.Year == 1984 && song.Composer == "Roger Taylor", "Map SAM albumyear and composer.");
Check(song.Artwork is null, "Local SAM paths must not be used as web artwork.");
var artist = (await repository.Artists(id: 1)).Single();
Check(artist.Name == "Queen" && artist.Biography.Contains("2 tracks across 2 albums"), "Group SAM artists.");
Check((await repository.Songs(artist: 1)).Count == 2, "Find all songs by representative artist ID.");
Check((await repository.Songs("% ")).Count == 1, "Percent search is literal.");
Check((await repository.Songs("Hidden advert")).Count == 0, "Exclude ads.");
Check((await repository.Songs("' OR 1=1 --")).Count == 0, "Parameterized search input.");
Check(!(await repository.Songs()).Any(s => s.Id == 1), "Fixture artist must fall outside the first 100 songs.");
var matched = await repository.BroadcastSongs([new Track("Radio Ga Ga", "Queen", null, null)]);
Check(matched.Single().Id == 1, "Resolve broadcast to existing SAM ID.");
Check((await repository.Songs(id: 999999)).Count == 0, "Unknown songs are absent.");
Console.WriteLine("PASS: SAM mapping, grouped artists, safe input, ad filtering, broadcast matching, missing records.");
