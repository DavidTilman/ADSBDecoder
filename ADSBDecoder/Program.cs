namespace ADSBDecoder;

internal class Program
{
    static void Main(string[] args)
    {
        const int ChunkSamples = 1 << 17;  // 131072 samples = 262144 bytes
        const int ChunkLength = ChunkSamples * 2;

        if (!Radio.DeviceExists)
        {
            Console.Error.WriteLine("No RTL-SDR found");
            return;
        }

        Radio radio = new Radio(0, ChunkLength);
        Console.Clear();
        
        AircraftRegistry registry = new AircraftRegistry();

        while(true)
        {

            if (!radio.ReadChunk() || radio.Chunk is null)
                return;

            ushort[] magnitudes = ChunkConverter.Magnitudes(radio.Chunk);
            
            foreach (AdsbMessage message in FrameDecoder.ScanChunk(magnitudes))
            {
                registry.ConsumeMessage(message);
            }

            Console.SetCursorPosition(0, 0);
            Console.Write(registry.ToString());
        }
    }
}
