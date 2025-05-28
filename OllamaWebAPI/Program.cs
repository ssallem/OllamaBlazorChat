using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// ✅ HTTP만 사용, 포트 강제 지정 (예: 5078)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenLocalhost(5078); // HTTP 전용
});

builder.Configuration.AddJsonFile(
    "appsettings.local.json",
    optional: true,
    reloadOnChange: true);

// Swagger 추가
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var chatEndpoint = new Uri(builder.Configuration["AI:Ollama:Chat:Endpoint"] ?? "http://localhost:11434/");
var embeddingEndpoint = new Uri(builder.Configuration["AI:Ollama:Embedding:Endpoint"] ?? "http://localhost:11434/");
var chatModelId = builder.Configuration["AI:Ollama:Chat:ModelId"];
var embeddingModelId = builder.Configuration["AI:Ollama:Embedding:ModelId"];

builder.Services.AddChatClient(new OllamaChatClient(chatEndpoint, chatModelId));
builder.Services.AddEmbeddingGenerator(new OllamaEmbeddingGenerator(embeddingEndpoint, embeddingModelId));

var app = builder.Build();

// Swagger 미들웨어 추가
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Ollama API V1");
        c.RoutePrefix = "apis"; // Swagger UI 경로 설정
    }); // 기본 주소: /swagger/index.html
}

// 예외 처리 포함한 /chat 엔드포인트
app.MapPost("/chat", async (IChatClient client, [FromBody] ChatRequest request) =>
{
    try
    {
        // 가장 최근 메세지만 추출 (예: 메세지 히스토리 중 마지막)        
        var lastMessage = request.messages.LastOrDefault()?.content ?? "";
        var result = await client.GetResponseAsync(lastMessage);
        Console.WriteLine(result);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ /chat 요청 처리 중 오류 발생:");
        Console.WriteLine(ex.ToString());
        return Results.Problem("서버 내부 오류가 발생했습니다.");
    }
});

// 예외 처리 포함한 /embedding 엔드포인트
app.MapPost("/embedding", async (IEmbeddingGenerator<string, Embedding<float>> client, [FromBody] string message) =>
{
    try
    {
        var result = await client.GenerateEmbeddingAsync(message);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ /embedding 요청 처리 중 오류 발생:");
        Console.WriteLine(ex.ToString());
        return Results.Problem("서버 내부 오류가 발생했습니다.");
    }
});
app.Run();
