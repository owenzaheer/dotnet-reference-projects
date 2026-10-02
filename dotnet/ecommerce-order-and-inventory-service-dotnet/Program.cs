using System.Text.Json.Nodes;
using Demo;
var builder=WebApplication.CreateBuilder(args);
var app=builder.Build();
var config=JsonNode.Parse(File.ReadAllText(Path.Combine(app.Environment.ContentRootPath,"project.json")))!.AsObject();
var engine=new Engine();
app.MapGet("/api/config",()=>Results.Json(config));
app.MapGet("/api/health",()=>Results.Json(new{status="ok",project=config["id"]!.GetValue<string>(),storage="in-memory synthetic fixture"}));
app.MapGet("/api/state",(HttpRequest r)=>r.Headers.Authorization=="Bearer local-operator"?Results.Json(engine.State()):Results.Json(new{detail="Operator required"},statusCode:403));
app.MapPost("/api/action",(JsonObject body,HttpRequest request)=>{
 var role=request.Headers.Authorization.ToString() switch {"Bearer local-learner"=>"learner","Bearer local-operator"=>"operator","Bearer local-instructor"=>"instructor",_=>null};
 if(role is null)return Results.Json(new{detail="Explicit local role token required"},statusCode:401);
 try{var action=body["action"]?.GetValue<string>()??"";if(!config["actions"]!.AsArray().Any(x=>x!.GetValue<string>()==action))throw new Problem(404,"Action unavailable in this project");if(body["payload"] is not JsonObject p)throw new Problem(422,"Object payload required");return Results.Json(engine.Command(action,p,role));}
 catch(Problem e){return Results.Json(new{detail=e.Message},statusCode:e.Status);}
 catch(InvalidOperationException){return Results.Json(new{detail="Invalid payload value types"},statusCode:422);}
});
app.MapGet("/",()=>Results.Content(File.ReadAllText(Path.Combine(app.Environment.ContentRootPath,"../../ui/console.html")).Replace("__CONFIG__",config.ToJsonString()),"text/html"));
app.Run();
