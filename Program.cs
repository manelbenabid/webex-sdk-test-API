using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseHttpsRedirection();

var accessToken = builder.Configuration["Webex:AccessToken"];
if (string.IsNullOrEmpty(accessToken))
    throw new Exception("Missing Webex:AccessToken in appsettings.json");


// ===============================================================
// 1️⃣ Create Meeting
// ===============================================================
app.MapPost("/api/meetings", async (HttpClient httpClient) =>
{
    var meetingPayload = new
    {
        title = $"G2G Meeting {Guid.NewGuid()}",
        start = DateTime.UtcNow.AddMinutes(5).ToString("yyyy-MM-ddTHH:mm:ssZ"),
        end = DateTime.UtcNow.AddMinutes(35).ToString("yyyy-MM-ddTHH:mm:ssZ"),
        enabledJoinBeforeHost = true,
        joinBeforeHostMinutes = 15,
        unlockedMeetingJoinSecurity = "allowJoin"
    };

    var request = new HttpRequestMessage(HttpMethod.Post, "https://webexapis.com/v1/meetings");
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    request.Content = new StringContent(JsonSerializer.Serialize(meetingPayload), Encoding.UTF8, "application/json");

    var response = await httpClient.SendAsync(request);
    var json = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        return Results.Problem(json);

    return Results.Content(json, "application/json");
});


// ===============================================================
// 2️⃣ Join Meeting (with service app token – backend side validation)
// ===============================================================
app.MapPost("/api/meetings/join", async (HttpClient client, Dictionary<string, string> body) =>
{
    Console.WriteLine("body ==============" );
    foreach (KeyValuePair<string, string> entry in body)
        {
            Console.WriteLine($"Key: {entry.Key}, Value: {entry.Value}");
        }
    
    if (!body.TryGetValue("meetingId", out var meetingId) || !body.TryGetValue("password", out var meetingPassword))
        return Results.Problem("Missing meetingId or password");

    var joinPayload = new
    {
        meetingId,
        password = meetingPassword,
        joinDirectly = false,
        email = $"guest{Guid.NewGuid()}@appid.ciscospark.com",
        displayName = $"Guest {Guid.NewGuid().ToString()[..8]}"
    };

    Console.WriteLine("payload ==============" +  joinPayload);

    var joinReq = new HttpRequestMessage(HttpMethod.Post, "https://webexapis.com/v1/meetings/join")
    {
        Content = JsonContent.Create(joinPayload)
    };
    joinReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    var joinResp = await client.SendAsync(joinReq);
    var joinJson = await joinResp.Content.ReadAsStringAsync();

    if (!joinResp.IsSuccessStatusCode)
        return Results.Problem(joinJson);

    return Results.Content(joinJson, "application/json");
});


// ===============================================================
// 3️⃣ Generate Guest Token
// ===============================================================
app.MapPost("/api/guests/token", async (HttpClient client, Dictionary<string, string> body) =>
{
    if (!body.TryGetValue("subject", out var subject))
        subject = $"Guest-{Guid.NewGuid()}";

    var guestPayload = new
    {
        subject,
        displayName = body.ContainsKey("displayName") ? body["displayName"] : "Anonymous Guest"
    };

    var req = new HttpRequestMessage(HttpMethod.Post, "https://webexapis.com/v1/guests/token");
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    req.Content = JsonContent.Create(guestPayload);

    var resp = await client.SendAsync(req);
    var json = await resp.Content.ReadAsStringAsync();

    if (!resp.IsSuccessStatusCode)
        return Results.Problem(json);

    return Results.Content(json, "application/json");
});

app.Run();
