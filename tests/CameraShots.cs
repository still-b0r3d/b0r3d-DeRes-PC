using System;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using B0r3dDeRes.Camera;
using B0r3dDeRes.Engine;
using CameraFrame = B0r3dDeRes.Camera.Frame;

namespace B0r3dDeRes.Tests
{
    // b0r3dDeResTests.exe --shots <folder> camera: the live camera on a pretend camera holding up the
    // cat nap photo (no real camera is ever started), in light and dark: live on the Game Boy Camera,
    // live on the NES with a CRT and the Export section waiting for a snap, recording, the clip it
    // made, and a camera Windows keeps.
    static partial class TestMain
    {
        static void CameraShots(string folder, Look look)
        {
            Theme.Set(look);
            string name = look.ToString().ToLowerInvariant();
            void Shot(MainWindow w, string shot)
            {
                LayOut(w.Root, MainWidth, MainHeight);
                Pump(() => false, 60);
                LayOut(w.Root, MainWidth, MainHeight);
                Shoot(w.Root, MainWidth, MainHeight, 1, Path.Combine(folder, shot + "-" + name + ".png"));
            }
            // the cat nap as a camera's picture
            PictureSource cat = Pictures.Sample("cat-nap").Source;
            var photo = new CameraFrame();
            photo.Size(cat.Width, cat.Height);
            Buffer.BlockCopy(cat.Bgra, 0, photo.Pixels, 0, photo.Pixels.Length);

            MainWindow live = NewMain();
            WaitForPicture(live);
            live.Cameras = () => new PictureCameras("Desk camera", photo);
            live.StartCamera();
            Pump(() => live.LiveFrames >= 3, 10000);
            live.Cards.Children.OfType<RadioButton>().First(c => (string)c.Tag == "gameboy-camera").IsChecked = true;
            int was = live.LiveFrames;
            Pump(() => live.LiveFrames > was + 2 && live.LiveOnScreen?.Profile.Id == "gameboy-camera", 10000);
            Shot(live, "camera-live");

            // the NES with a CRT, live, and the Export section saying saves wait for a snap
            live.Cards.Children.OfType<RadioButton>().First(c => (string)c.Tag == "nes").IsChecked = true;
            live.ScreenSection.IsExpanded = true;
            live.ScreenButtons.Children.OfType<RadioButton>().First(c => (string)c.Tag == Screens.Crt).IsChecked = true;
            live.HardwareSection.IsExpanded = false;
            live.ExportSection.IsExpanded = true;
            was = live.LiveFrames;
            Pump(() => live.LiveFrames > was + 3, 10000);
            ScrollTo(live, live.LiveNote);
            Shot(live, "camera-live-crt");

            // recording, then the clip it made
            live.ExportSection.IsExpanded = false;
            live.ScreenSection.IsExpanded = false;
            ScrollTo(live, live.CameraPanel);
            live.RecordCamera();
            Pump(() => live.CameraOn && ((string)live.RecordButton.Content).StartsWith("Recording 2"), 5000);
            Shot(live, "camera-recording");
            Pump(() => !live.CameraOn, 10000);
            WaitForPicture(live);
            ScrollTo(live, live.PictureInfo);
            Shot(live, "camera-clip");
            live.Close();

            // a camera Windows keeps: where to allow it, under the button
            MainWindow refused = NewMain();
            WaitForPicture(refused);
            refused.Cameras = () => new FakeCameras(new FakeCamera { Name = "Desk camera", OpenTrouble = Webcams.Problem(unchecked((int)0x80070005), "Windows didn't start it") });
            refused.StartCamera();
            Pump(() => !refused.CameraOn, 5000);
            Settle(refused);
            Shot(refused, "camera-refused");
            refused.Close();
        }
    }
}
