using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NexoLoL.Core;

namespace NexoLoL.Desktop;

public partial class MainWindow
{
    GameAccount? publicProfile;
    CancellationTokenSource? profileLookup;
    string lookupText="",lookupMessage="";

    void AddProfileLookup(StackPanel top)
    {
        var row=new WrapPanel{Margin=new Thickness(0,12,0,0)};
        var input=new TextBox{Text=lookupText,MinWidth=200,MaxLength=41,ToolTip="Riot ID, incluindo # e tag"};
        System.Windows.Automation.AutomationProperties.SetName(input,"Riot ID para consulta no Brasil");
        input.TextChanged+=(_,_)=>lookupText=input.Text;
        row.Children.Add(input);
        var find=Button(profileLookup is null?"Consultar Riot ID · BR":"Consultando…",()=>_=LookupProfile());
        find.IsEnabled=profileLookup is null;
        row.Children.Add(find);
        if(publicProfile is not null || profileLookup is not null)
            row.Children.Add(Button("Voltar ao meu perfil",()=>{ClearPublicProfile();RenderAccount();}));
        top.Children.Add(row);
        if(lookupMessage.Length>0)top.Children.Add(Text(lookupMessage,12,false,Muted,top:8));
    }

    void ClearPublicProfile()
    {
        profileLookup?.Cancel();profileLookup=null;
        publicProfile=null;lookupMessage="";lookupText="";
    }

    async Task LookupProfile()
    {
        if(profileLookup is not null)return;
        if(!RiotId.TrySplit(lookupText,out var name,out var tag)){
            lookupMessage="Digite o Riot ID no formato Nome#TAG.";RenderAccount();return;
        }
        using var request=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        profileLookup=request;
        publicProfile=null;lookupMessage="Buscando perfil e histórico no Brasil…";RenderAccount();
        try{
            var uri=BardoApiUri("/v1/br1/profile?riotId="+Uri.EscapeDataString(name+"#"+tag));
            byte[] json=await remote.BytesAsync(uri.AbsoluteUri,512_000,request.Token);
            var found=JsonSerializer.Deserialize<GameAccount>(json,JsonSerializerOptions.Web);
            if(found?.Ranks is null || found.Matches is null || string.IsNullOrWhiteSpace(found.RiotId))throw new InvalidDataException();
            if(request.IsCancellationRequested)return;
            publicProfile=found;lookupMessage="";
        }catch(OperationCanceledException) when(request.IsCancellationRequested){}
        catch(Exception e) when(e is HttpRequestException or IOException or InvalidDataException or JsonException or TimeoutException){
            if(request.IsCancellationRequested)return;
            lookupMessage=e switch {
                HttpRequestException {StatusCode:HttpStatusCode.NotFound}=>"Perfil de League não encontrado no Brasil. Confira o nome e a tag.",
                HttpRequestException {StatusCode:HttpStatusCode.TooManyRequests}=>"Limite de consultas atingido. Tente novamente em instantes.",
                HttpRequestException {StatusCode:HttpStatusCode.ServiceUnavailable}=>"A API do Bardola está sem acesso à Riot no momento.",
                _=>"Não foi possível consultar a API do Bardola. Tente novamente mais tarde."
            };
        }finally{
            if(profileLookup==request){profileLookup=null;if(!lifetime.IsCancellationRequested)RenderAccount();}
        }
    }

    static Uri BardoApiUri(string path)
    {
        var endpoint=Environment.GetEnvironmentVariable("BARDO_API_URL")??"http://127.0.0.1:5219";
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var host) || host.UserInfo.Length!=0 ||
            !(host.Scheme=="https" || host.Scheme=="http" && host.IsLoopback))throw new InvalidDataException();
        return new Uri(host,path);
    }
}
