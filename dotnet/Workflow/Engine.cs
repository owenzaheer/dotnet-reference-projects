using System.Text.Json.Nodes;
namespace Demo;
public sealed class Problem(int status,string message):Exception(message){public int Status{get;}=status;}
public sealed class Engine {
 private readonly object gate=new();
 private int stock=10, stockVersion=0;
 private readonly Dictionary<string,JsonObject> orders=new(),grades=new(),files=new();
 private readonly List<JsonObject> outbox=new(),audit=new();
 static string Text(JsonObject p,string k)=>p[k]?.GetValue<string>() is {Length:>0 and <101} value?value:throw new Problem(422,k+" is required");
 static int Int(JsonObject p,string k,int min=0)=>p[k] is JsonValue v&&v.TryGetValue<int>(out var n)&&n>=min?n:throw new Problem(422,k+" must be an integer >= "+min);
 static JsonObject Find(Dictionary<string,JsonObject> d,string id)=>d.TryGetValue(id,out var r)?r:throw new Problem(404,"Record not found");
 static JsonObject Copy(JsonObject r)=>(JsonObject)r.DeepClone();
 static JsonArray Rows(IEnumerable<JsonObject> source)=>new(source.Select(x=>(JsonNode)x.DeepClone()).ToArray());
 public JsonObject State(){lock(gate)return new(){["stock"]=stock,["stockVersion"]=stockVersion,["orders"]=Rows(orders.Values),["grades"]=Rows(grades.Values),["files"]=Rows(files.Values),["outbox"]=Rows(outbox),["audit"]=Rows(audit)};}
 public JsonObject Command(string action,JsonObject p,string role){lock(gate){var r=Execute(action,p,role);audit.Add(new(){["role"]=role,["action"]=action,["payload"]=p.DeepClone()});return Copy(r);}}
 private JsonObject Execute(string action,JsonObject p,string role){
 if(action=="reserve"){
  var id=Text(p,"id");var sku=Text(p,"sku");var qty=Int(p,"quantity",1);
  if(orders.TryGetValue(id,out var old)){if(old["quantity"]!.GetValue<int>()!=qty||old["sku"]!.GetValue<string>()!=sku)throw new Problem(409,"Conflicting idempotency key");return old;}
  if(sku!="BOOK"||Int(p,"version")!=stockVersion||qty>stock)throw new Problem(409,"Insufficient stock or stale version");
  stock-=qty;stockVersion++;var row=new JsonObject{["id"]=id,["sku"]=sku,["quantity"]=qty,["status"]="reserved",["version"]=0,["expires"]=DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()};orders.Add(id,row);outbox.Add(new(){["id"]=id,["event"]="order.reserved",["delivered"]=false});return row;
 }
 if(new[]{"ship","cancel","refund"}.Contains(action)){
  if(role!="operator")throw new Problem(403,"Operator required");var row=Find(orders,Text(p,"id"));var target=action=="ship"?"shipped":action=="cancel"?"cancelled":"refunded";
  if(row["status"]!.GetValue<string>()==target)return row;
  if(row["status"]!.GetValue<string>()!=(action=="refund"?"shipped":"reserved")||row["version"]!.GetValue<int>()!=Int(p,"version"))throw new Problem(409,"Invalid transition or stale version");
  if(action=="ship"&&row["expires"]!.GetValue<long>()<DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new Problem(409,"Reservation expired");
  if(action=="cancel"){stock+=row["quantity"]!.GetValue<int>();stockVersion++;}row["status"]=target;row["version"]=row["version"]!.GetValue<int>()+1;outbox.Add(new(){["id"]=row["id"]!.DeepClone(),["event"]="order."+target,["delivered"]=false});return row;
 }
 if(action=="relay"){
  if(role!="operator")throw new Problem(403,"Operator required");if(p["simulateFailure"]?.GetValue<bool>()==true)throw new Problem(503,"Fixture relay outage; messages retained");
  var pending=outbox.Where(x=>!x["delivered"]!.GetValue<bool>()).ToList();var result=new JsonObject{["delivered"]=Rows(pending)};foreach(var row in pending)row["delivered"]=true;return result;
 }
 if(action=="submit"){
  if(role is not ("learner" or "instructor"))throw new Problem(403,"Learner required");var id=Text(p,"id");var student=Text(p,"student");var deadline=p["deadline"]?.GetValue<long>()??0;
  if(deadline<DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new Problem(422,"Deadline passed");if(p["answers"] is not JsonArray a||a.Count!=3||a.Any(x=>x is not JsonValue v||!v.TryGetValue<string>(out _)))throw new Problem(422,"Three answer strings required");
  if(grades.ContainsKey(id))throw new Problem(409,"Repeated submission");var expected=new[]{"transaction","idempotency","audit"};var score=a.Select((x,i)=>x!.GetValue<string>().Trim().ToLowerInvariant()==expected[i]?1:0).Sum();
  var row=new JsonObject{["id"]=id,["student"]=student,["score"]=score,["feedback"]="",["status"]="pending",["version"]=0};grades.Add(id,row);return row;
 }
 if(action is "feedback" or "publish"){
  if(role!="instructor")throw new Problem(403,"Instructor required");var row=Find(grades,Text(p,"id"));if(row["version"]!.GetValue<int>()!=Int(p,"version"))throw new Problem(409,"Stale assessment version");
  if(action=="feedback"){row["feedback"]=Text(p,"feedback");row["status"]="review";}else{if(row["status"]!.GetValue<string>()!="review")throw new Problem(409,"Feedback review required");row["status"]="published";}row["version"]=row["version"]!.GetValue<int>()+1;return row;
 }
 if(action=="ingest"){
  if(role!="operator")throw new Problem(403,"Operator required");var id=Text(p,"id");if(p["rows"] is not JsonArray rows||rows.Count is <1 or >100)throw new Problem(422,"1-100 rows required");
  foreach(var item in rows){if(item is not JsonObject r)throw new Problem(422,"Object row required");Text(r,"sku");Int(r,"quantity",1);}
  var content=rows.ToJsonString();if(files.TryGetValue(id,out var old)){if(old["content"]!.GetValue<string>()!=content)throw new Problem(409,"File id reused with different content");return old;}
  var file=new JsonObject{["id"]=id,["content"]=content,["status"]="pending",["attempts"]=0};files.Add(id,file);return file;
 }
 if(action=="replay"){
  if(role!="operator")throw new Problem(403,"Operator required");var row=Find(files,Text(p,"id"));if(row["status"]!.GetValue<string>()=="processed")return row;
  row["attempts"]=row["attempts"]!.GetValue<int>()+1;if(p["simulateFailure"]?.GetValue<bool>()==true){row["status"]="retry";return row;}row["status"]="processed";return row;
 }
 throw new Problem(404,"Unknown action");
 }
}
