// Byte count includes every stdout/stderr byte, including CR/LF and encoding bytes.
// No decoding, line buffering or content retention. Cap is sampled by supervisor.
sealed class ExactOutputBudget(long limit) {
 long bytes;
 public long Bytes=>Interlocked.Read(ref bytes);
 public bool Overflow=>Bytes>limit;
 public async Task Drain(Stream stream){var buffer=new byte[4096];int n;while((n=await stream.ReadAsync(buffer))!=0)Interlocked.Add(ref bytes,n);}
}

static class FinalSupervisorVerdict {
 public static bool Pass(bool candidate,bool cleanup,bool eof,bool overflow,int forced,IReadOnlyList<int> exits)=>candidate&&cleanup&&eof&&!overflow&&forced==0&&exits.Count==2&&exits.All(x=>x==0);
}
