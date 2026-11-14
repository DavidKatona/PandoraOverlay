using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PandoraOverlay.Games.Engine;
using Xunit;
using Xunit.Abstractions;

namespace PandoraOverlay.Games.Tests;

/// <summary>
/// What a frame of a playfield costs on this PC (A05's first step, the plan's
/// rendering section): a 532 x 456 board composed the way a board is (a
/// clear, 121 cell fills of 44 x 50, 30 sprites from the engine's test
/// atlas), scaled to the device size at 1.0, 1.5 and 2.0 the way
/// <see cref="PixelSurface"/> scales it (one <see cref="ScalePlan"/> per size,
/// made before the frames), and written into a <see cref="WriteableBitmap"/> of
/// that size with <c>WritePixels</c>, on an STA thread: 300 frames at each
/// scale, after 20 frames of warm-up that are not counted (the first frames
/// pay for the JIT, which a game pays once per launch).
/// <para>
/// The rule it decides: under 8 ms a frame on average at 2.0, the surface
/// presents whole frames; over, only the rows that changed. Run it with
/// <c>GAMES_MEASURE=1</c> (it returns at once otherwise: a timing is not a
/// test, and CI's machines are not the owner's PC) and read the numbers with
/// <c>--logger "console;verbosity=detailed"</c>. Stopwatch is the tests'
/// alone: the forbidden list keeps it out of the engine and the games (A29).
/// </para>
/// <para>
/// What it does not measure: WPF's own composition of the bitmap onto the
/// screen and the GPU's upload of it, which only a shown window over the real
/// game pays (the owner's check in game).
/// </para>
/// </summary>
public class PresentCost
{
    private const int Width = 532, Height = 456, Frames = 300, WarmUp = 20;

    private readonly ITestOutputHelper _output;

    public PresentCost(ITestOutputHelper output) => _output = output;

    [Fact]
    public void WhatAFrameCosts()
    {
        if (Environment.GetEnvironmentVariable("GAMES_MEASURE") != "1")
            return;

        var report = new StringBuilder();
        Sta.Run(() =>
        {
            var atlas = TestAtlas.Load();
            var block = atlas["block/stand/0"];
            var glass = atlas["glass/stand/0"];
            var buffer = new PixelBuffer(Width, Height);

            report.AppendLine(CultureInfo.InvariantCulture, $"PresentCost: {Width} x {Height}, {Frames} frames per scale after {WarmUp} uncounted, ms per frame");
            report.AppendLine("scale  device     compose avg/worst   scale avg/worst   write avg/worst   total avg/worst");
            foreach (var scale in new[] { 1.0, 1.5, 2.0 })
            {
                var (dw, dh) = Presenter.DeviceSize(Width, Height, scale, scale);
                var plan = new ScalePlan(Width, Height, dw, dh);
                var device = new uint[dw * dh];
                var bitmap = new WriteableBitmap(dw, dh, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null);
                var rect = new Int32Rect(0, 0, dw, dh);

                var compose = new Times();
                var scaling = new Times();
                var write = new Times();
                var total = new Times();
                var clock = new Stopwatch();
                for (var frame = 0; frame < WarmUp + Frames; frame++)
                {
                    var counted = frame >= WarmUp;
                    clock.Restart();
                    Compose(buffer, block, glass, frame);
                    var a = clock.Elapsed.TotalMilliseconds;
                    Presenter.Scale(buffer.Pixels, device, plan);
                    var b = clock.Elapsed.TotalMilliseconds;
                    bitmap.WritePixels(rect, device, dw * 4, 0);
                    var c = clock.Elapsed.TotalMilliseconds;
                    if (!counted) continue;
                    compose.Add(a);
                    scaling.Add(b - a);
                    write.Add(c - b);
                    total.Add(c);
                }

                report.AppendLine(CultureInfo.InvariantCulture,
                    $"{scale,5:0.0}  {dw,4} x {dh,-4}  {compose}       {scaling}     {write}     {total}");
            }
        });
        _output.WriteLine(report.ToString());
    }

    /// <summary>
    /// A board's frame: the ground cleared, 11 x 11 cells filled (44 x 50
    /// each, odd rows shifted half a cell, rows 37 apart, as a hex board's
    /// tiles overlap), and 30 creatures, half of them flipped, a third with a
    /// glass mark over them. The frame number moves the creatures, so no two
    /// frames are the same picture.
    /// </summary>
    private static void Compose(PixelBuffer buffer, Sprite block, Sprite glass, int frame)
    {
        buffer.Clear(PixelBuffer.Colour(12, 18, 24));
        for (var row = 0; row < 11; row++)
            for (var column = 0; column < 11; column++)
            {
                var x = column * 44 + (row % 2 == 1 ? 22 : 0);
                var y = row * 37;
                var shade = (byte)(60 + (row * 11 + column) % 7 * 12);
                buffer.Fill(x, y, 44, 50, PixelBuffer.Colour((byte)(shade / 2), shade, (byte)(shade / 3)));
            }
        for (var i = 0; i < 30; i++)
        {
            var x = 20 + (i * 97 + frame * 3) % 500;
            var y = 30 + (i * 61) % 420;
            buffer.Draw(block, x, y, flip: i % 2 == 1);
            if (i % 3 == 0)
                buffer.Draw(glass, x, y - 26, opacity: 200);
        }
    }

    /// <summary>The average and the worst of a set of timings, in ms.</summary>
    private sealed class Times
    {
        private double _sum, _worst;
        private int _count;

        public void Add(double ms)
        {
            _sum += ms;
            _count++;
            if (ms > _worst) _worst = ms;
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0,5:0.00} / {1,5:0.00}", _count == 0 ? 0 : _sum / _count, _worst);
    }
}
