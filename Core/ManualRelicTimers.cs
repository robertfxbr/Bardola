namespace NexoLoL.Core;

// Relíquias não têm evento público de coleta. Estes contadores são marcados pelo usuário.
public sealed class ManualRelicTimers
{
    readonly Dictionary<int,double> deadlines=[];
    public void Mark(int relic,double gameTime,double duration){
        if(relic is <0 or >3||!double.IsFinite(gameTime)||gameTime<=0||!double.IsFinite(duration)||duration is <30 or >300)throw new ArgumentOutOfRangeException(nameof(duration));
        deadlines[relic]=gameTime+duration;
    }
    public double? Remaining(int relic,double gameTime)=>deadlines.TryGetValue(relic,out double end)?Math.Max(0,end-gameTime):null;
    public void Reset()=>deadlines.Clear();
}
