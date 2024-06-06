using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinSW.Core;

internal class StreamMirror(Stream source)
{
    private const int BufferSize = 1024;
    private readonly Stream source = source;

    public async void MirrorStreams(Stream[] destinations)
    {
        byte[] buffer = new byte[BufferSize];
        int bytesRead;
        while ((bytesRead = await this.source.ReadAsync(buffer, 0, buffer.Length)) != 0)
        {
            foreach (var dest in destinations)
            {
                await dest.WriteAsync(buffer, 0, bytesRead);
            }
        }
    }
}
