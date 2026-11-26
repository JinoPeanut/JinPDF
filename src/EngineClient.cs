using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

sealed class EngineClient : IDisposable
{
    readonly object gate = new object();
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    readonly string root = AppDomain.CurrentDomain.BaseDirectory;
    Process process;
    StreamWriter input;
    bool disposed;

    public Dictionary<string, object> Run(object request, Action<Dictionary<string, object>> progress = null)
    {
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException("EngineClient");
            if (process == null || process.HasExited) Start();
            input.WriteLine(json.Serialize(request));
            input.Flush();
            string line;
            while ((line = process.StandardOutput.ReadLine()) != null)
            {
                var reply = json.Deserialize<Dictionary<string, object>>(line);
                if (reply.ContainsKey("error")) throw new InvalidOperationException((string)reply["error"]);
                if (reply.ContainsKey("result")) return (Dictionary<string, object>)reply["result"];
                if (progress != null) progress((Dictionary<string, object>)reply["progress"]);
            }
            throw new InvalidOperationException("PDF 처리 프로그램이 종료되었습니다.");
        }
    }

    void Start()
    {
        if (process != null) process.Dispose();
        var python = Path.Combine(root, "runtime", "python.exe");
        var script = Path.Combine(root, "engine.py");
        var start = new ProcessStartInfo(python, "-I -B -X utf8 \"" + script + "\"") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardInput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        process = Process.Start(start);
        input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false));
        process.BeginErrorReadLine();
    }

    public Bitmap Render(string path, int page, int pixels, string temporary, bool sharpen = false)
    {
        string output = Path.Combine(temporary, Guid.NewGuid().ToString("N") + ".png");
        try
        {
            Run(new { op = "render", path = path, page = page, pixels = pixels, sharpen = sharpen, @out = output });
            using (var decoded = new Bitmap(output)) return new Bitmap(decoded);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    input.Close();
                    if (!process.WaitForExit(1000)) process.Kill();
                }
            }
            finally { process.Dispose(); }
        }
    }
}
