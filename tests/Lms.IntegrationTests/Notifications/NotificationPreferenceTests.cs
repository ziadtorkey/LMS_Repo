using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lms.Domain.Notifications;
using Lms.Infrastructure.Notifications;
using Lms.IntegrationTests.Infrastructure;
using Lms.Shared;
using Lms.Shared.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Lms.IntegrationTests.Notifications;

[Collection(ApiCollection.Name)]
public sealed class NotificationPreferenceTests(LmsApiFactory factory)
{
    private static readonly string Event = NotificationEvents.OrganizationPolicyChanged;

    private HttpClient Client => factory.CreateClient();

    private static HttpRequestMessage Request(HttpMethod method, Guid user, object? body = null)
    {
        var request = new HttpRequestMessage(method, Routes.MyNotificationPreferences);
        request.Headers.Add(TestAuthHandler.UserHeader, user.ToString());
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private async Task<List<NotificationPreferenceDto>> GetAsync(Guid user)
    {
        var response = await Client.SendAsync(Request(HttpMethod.Get, user));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<NotificationPreferenceDto>>())!;
    }

    private Task<HttpResponseMessage> PutAsync(Guid user, params NotificationPreferenceDto[] preferences) =>
        Client.SendAsync(Request(HttpMethod.Put, user, new SetNotificationPreferencesRequest(preferences)));

    private static NotificationPreferenceDto Pref(string channel, bool enabled, string? eventKey = null) =>
        new(eventKey ?? Event, channel, enabled, Locked: channel == NotificationChannels.InApp);

    [Fact]
    public async Task Defaults_are_returned_without_rows()
    {
        var all = await GetAsync(Guid.NewGuid());

        // Every catalog event x channel, in a stable order.
        Assert.Equal(NotificationEvents.All.Count * NotificationChannels.All.Count, all.Count);
        Assert.Equal(all.Count, all.Select(x => (x.EventKey, x.Channel)).Distinct().Count());
        foreach (var e in NotificationEvents.All)
        {
            var forEvent = all.Where(x => x.EventKey == e.Key).ToDictionary(x => x.Channel);
            Assert.True(forEvent[NotificationChannels.InApp].Enabled);
            Assert.True(forEvent[NotificationChannels.InApp].Locked);
            Assert.True(forEvent[NotificationChannels.Email].Enabled);
            Assert.False(forEvent[NotificationChannels.Push].Enabled);
            Assert.All(forEvent.Where(x => x.Key != NotificationChannels.InApp), x => Assert.False(x.Value.Locked));
        }
    }

    [Fact]
    public async Task Put_then_get_round_trips()
    {
        var user = Guid.NewGuid();

        var put = await PutAsync(user,
            Pref(NotificationChannels.Email, false),
            Pref(NotificationChannels.Push, true),
            Pref(NotificationChannels.InApp, true));

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var all = (await GetAsync(user)).Where(x => x.EventKey == Event).ToDictionary(x => x.Channel);
        Assert.False(all[NotificationChannels.Email].Enabled);
        Assert.True(all[NotificationChannels.Push].Enabled);
        Assert.True(all[NotificationChannels.InApp].Enabled);
        // Other events keep their defaults.
        var other = (await GetAsync(user)).Where(x => x.EventKey == NotificationEvents.AccessRoleAssigned).ToDictionary(x => x.Channel);
        Assert.True(other[NotificationChannels.Email].Enabled);
    }

    [Fact]
    public async Task Put_replaces_the_callers_rows_so_missing_entries_return_to_defaults()
    {
        var user = Guid.NewGuid();
        await PutAsync(user, Pref(NotificationChannels.Email, false), Pref(NotificationChannels.Push, true));

        await PutAsync(user, Pref(NotificationChannels.InApp, true));

        var all = (await GetAsync(user)).Where(x => x.EventKey == Event).ToDictionary(x => x.Channel);
        Assert.True(all[NotificationChannels.Email].Enabled);  // back to the default
        Assert.False(all[NotificationChannels.Push].Enabled);  // back to the default
        Assert.True(all[NotificationChannels.InApp].Enabled);

        // An empty list resets everything.
        Assert.Equal(HttpStatusCode.NoContent, (await PutAsync(user)).StatusCode);
        Assert.False((await GetAsync(user)).Single(x => x.EventKey == Event && x.Channel == NotificationChannels.Push).Enabled);
    }

    [Fact]
    public async Task Disabling_in_app_is_400()
    {
        var user = Guid.NewGuid();

        var response = await PutAsync(user, Pref(NotificationChannels.InApp, false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.in_app_required, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.True((await GetAsync(user)).Single(x => x.EventKey == Event && x.Channel == NotificationChannels.InApp).Enabled);
    }

    [Fact]
    public async Task Unknown_event_channel_and_duplicates_are_400_and_change_nothing()
    {
        var user = Guid.NewGuid();
        await PutAsync(user, Pref(NotificationChannels.Push, true));

        var bad = new[]
        {
            new[] { Pref("Carrier-Pigeon", true) },
            new[] { Pref(NotificationChannels.Email, true, "Nope.Unknown") },
            new[] { Pref(NotificationChannels.Email, false), Pref(NotificationChannels.Email, true) },
            new[] { Pref("email", false) }, // channel names are exact
        };
        foreach (var preferences in bad)
        {
            var response = await PutAsync(user, preferences);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(ProblemCodes.validation_failed, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }

        // The earlier saved state is intact after the refused requests.
        Assert.True((await GetAsync(user)).Single(x => x.EventKey == Event && x.Channel == NotificationChannels.Push).Enabled);
        // A JSON request with an empty body is refused too (no preferences at all is not "nothing to change").
        var emptyRequest = Request(HttpMethod.Put, user);
        emptyRequest.Content = new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await Client.SendAsync(emptyRequest)).StatusCode);
    }

    [Fact]
    public async Task Null_items_in_the_list_are_400_validation_failed()
    {
        var user = Guid.NewGuid();
        var request = Request(HttpMethod.Put, user);
        request.Content = new StringContent("{\"preferences\":[null]}", System.Text.Encoding.UTF8, "application/json");

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ProblemCodes.validation_failed, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task User_cannot_see_or_change_another_users_preferences()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await PutAsync(alice, Pref(NotificationChannels.Email, false), Pref(NotificationChannels.Push, true));

        // Bob sees defaults, whatever Alice saved, and his own save does not touch hers.
        var bobSees = (await GetAsync(bob)).Where(x => x.EventKey == Event).ToDictionary(x => x.Channel);
        Assert.True(bobSees[NotificationChannels.Email].Enabled);
        Assert.False(bobSees[NotificationChannels.Push].Enabled);
        await PutAsync(bob, Pref(NotificationChannels.Push, true));

        var aliceSees = (await GetAsync(alice)).Where(x => x.EventKey == Event).ToDictionary(x => x.Channel);
        Assert.False(aliceSees[NotificationChannels.Email].Enabled);
        Assert.True(aliceSees[NotificationChannels.Push].Enabled);

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        Assert.Equal(2, await db.Set<NotificationPreference>().CountAsync(x => x.UserId == alice));
        Assert.Equal(1, await db.Set<NotificationPreference>().CountAsync(x => x.UserId == bob));
    }

    [Fact]
    public async Task Preference_endpoints_require_authentication()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client.GetAsync(Routes.MyNotificationPreferences)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client.PutAsJsonAsync(Routes.MyNotificationPreferences, new SetNotificationPreferencesRequest([]))).StatusCode);
    }

    [Fact]
    public async Task IsEnabled_reflects_saved_value()
    {
        var user = Guid.NewGuid();
        await PutAsync(user, Pref(NotificationChannels.Email, false), Pref(NotificationChannels.Push, true));

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        var preferences = new NotificationPreferences(db);

        Assert.False(await preferences.IsEnabledAsync(user, Event, NotificationChannels.Email));
        Assert.True(await preferences.IsEnabledAsync(user, Event, NotificationChannels.Push));
        // Defaults for what was never saved, per user and per event.
        Assert.True(await preferences.IsEnabledAsync(user, Event, NotificationChannels.InApp));
        Assert.True(await preferences.IsEnabledAsync(user, NotificationEvents.AccessRoleAssigned, NotificationChannels.Email));
        Assert.True(await preferences.IsEnabledAsync(Guid.NewGuid(), Event, NotificationChannels.Email));
    }

    [Fact]
    public async Task In_app_is_always_on_even_if_a_row_says_otherwise()
    {
        var user = Guid.NewGuid();
        await using (var write = factory.CreateDbContext(TestCurrentContext.PlatformAdmin()))
        {
            write.Add(new NotificationPreference(user, Event, NotificationChannels.InApp, enabled: false));
            await write.SaveChangesAsync();
        }

        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());

        Assert.True(await new NotificationPreferences(db).IsEnabledAsync(user, Event, NotificationChannels.InApp));
        Assert.True((await GetAsync(user)).Single(x => x.EventKey == Event && x.Channel == NotificationChannels.InApp).Enabled);
    }

    [Fact]
    public async Task Unique_row_per_user_event_and_channel_is_enforced_by_the_database()
    {
        var user = Guid.NewGuid();
        await using var db = factory.CreateDbContext(TestCurrentContext.PlatformAdmin());
        db.Add(new NotificationPreference(user, Event, NotificationChannels.Email, true));
        db.Add(new NotificationPreference(user, Event, NotificationChannels.Email, false));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
