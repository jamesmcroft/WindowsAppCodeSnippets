// --------------------------------------------------------------------------------------------------------------------
// <copyright file="CameraCapture.cs" company="James Croft">
//   Copyright (c) 2015 James Croft. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Croft.Core.Helpers.Media
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Runtime.InteropServices.WindowsRuntime;
    using System.Threading.Tasks;

    using GalaSoft.MvvmLight;

    using Windows.Devices.Enumeration;
    using Windows.Graphics.Display;
    using Windows.Graphics.Imaging;
    using Windows.Media.Capture;
    using Windows.Media.Devices;
    using Windows.Media.MediaProperties;
    using Windows.Storage;
    using Windows.Storage.Streams;
    using Windows.UI.Xaml.Media.Imaging;

    /// <summary>
    /// The camera capture.
    /// </summary>
    public sealed class CameraCapture : ObservableObject, IDisposable
    {
        private MediaCapture _mediaCapture;

        private IMediaEncodingProperties _mediaEncodingProperties;

        private ImageEncodingProperties _imageEncodingProperties;

        private MediaEncodingProfile _videoEncodingProfile;

        private bool _isPreviewing;

        private bool _isDisposing;

        private bool _isFrontFacingAvailable;

        private bool _isRecording;

        private bool _isFlashAvailable;

        private bool _isTorchAvailable;

        private bool _isCameraAvailable;

        private readonly List<string> _supportedVideoFormats = new List<string> { "yuy2", "nv12", "rgb32", "rbg24" };

        /// <summary>
        /// Initializes a new instance of the <see cref="CameraCapture"/> class.
        /// </summary>
        public CameraCapture()
        {
            this.ActiveCamera = Panel.Back;
            this.IsCameraAvailable = true;
        }

        /// <summary>
        /// Gets the active camera.
        /// </summary>
        public Panel ActiveCamera { get; private set; }

        /// <summary>
        /// Gets the <see cref="MediaCapture"/> device settings.
        /// </summary>
        public VideoDeviceController Settings
        {
            get
            {
                return this._mediaCapture.VideoDeviceController;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether is camera available.
        /// </summary>
        public bool IsCameraAvailable
        {
            get
            {
                return this._isCameraAvailable;
            }
            set
            {
                this.Set(() => this.IsCameraAvailable, ref this._isCameraAvailable, value);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the current device has a front facing camera.
        /// </summary>
        public bool IsFrontFacingAvailable
        {
            get
            {
                return this._isFrontFacingAvailable;
            }
            set
            {
                this.Set(() => this.IsFrontFacingAvailable, ref this._isFrontFacingAvailable, value);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the active camera supports flash.
        /// </summary>
        public bool IsFlashAvailable
        {
            get
            {
                return this._isFlashAvailable;
            }
            set
            {
                this.Set(() => this.IsFlashAvailable, ref this._isFlashAvailable, value);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the <see cref="MediaCapture"/> is recording.
        /// </summary>
        public bool IsRecording
        {
            get
            {
                return this._isRecording;
            }
            set
            {
                this.Set(() => this.IsRecording, ref this._isRecording, value);
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the active camera supports torch.
        /// </summary>
        public bool IsTorchAvailable
        {
            get
            {
                return this._isTorchAvailable;
            }
            set
            {
                this.Set(() => this.IsTorchAvailable, ref this._isTorchAvailable, value);
            }
        }

        /// <summary>
        /// Initializes the <see cref="MediaCapture"/> element.
        /// </summary>
        /// <param name="primaryUse">
        /// The primary use for the camera.
        /// </param>
        /// <param name="videoQuality">
        /// The video quality (for recording only).
        /// </param>
        /// <returns>
        /// The <see cref="MediaCapture"/>.
        /// </returns>
        public async Task<MediaCapture> Initialize(
            CaptureUse primaryUse,
            VideoEncodingQuality videoQuality)
        {
            if (this._mediaCapture != null)
            {
                this.Dispose();
            }

            this.IsCameraAvailable = true;

            var camera = await this.GetCamera(this.ActiveCamera);

            this._mediaCapture = new MediaCapture();

            this._mediaCapture.Failed += this.OnMediaCaptureFailed;

            await
                this._mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings { VideoDeviceId = camera.Id });

            this._mediaCapture.VideoDeviceController.PrimaryUse = primaryUse;

            this._imageEncodingProperties = ImageEncodingProperties.CreateJpeg();
            this._videoEncodingProfile = MediaEncodingProfile.CreateMp4(videoQuality);

            this.SetEncodingProperties(primaryUse);

            var rotation = this.GetVideoRotation(DisplayInformation.GetForCurrentView().CurrentOrientation);

            if (primaryUse == CaptureUse.Photo)
            {
                await
                    this._mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(
                        MediaStreamType.Photo,
                        this._mediaEncodingProperties);

                this.IsFlashAvailable = this.Settings.FlashControl.Supported;
            }
            else if (primaryUse == CaptureUse.Video)
            {
                await
                    this._mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(
                        MediaStreamType.VideoPreview,
                        this._mediaEncodingProperties);

                await
                    this._mediaCapture.VideoDeviceController.SetMediaStreamPropertiesAsync(
                        MediaStreamType.VideoRecord,
                        this._mediaEncodingProperties);

                this.SetRecordOrientation(rotation);

                this.IsFlashAvailable = this.Settings.TorchControl.Supported;
            }

            this._mediaCapture.SetPreviewRotation(rotation);
            return this._mediaCapture;
        }

        private void SetRecordOrientation(VideoRotation rotation)
        {
            var videoProperties = this._videoEncodingProfile.Video;
            if (videoProperties != null)
            {
                var width = videoProperties.Width;
                var height = videoProperties.Height;

                switch (rotation)
                {
                    case VideoRotation.None:
                    case VideoRotation.Clockwise180Degrees:
                        if (height > width)
                        {
                            videoProperties.Height = width;
                            videoProperties.Width = height;
                        }
                        break;
                    case VideoRotation.Clockwise90Degrees:
                    case VideoRotation.Clockwise270Degrees:
                        if (width > height)
                        {
                            videoProperties.Height = width;
                            videoProperties.Width = height;
                        }
                        break;
                }
            }

            this._mediaCapture.SetRecordRotation(rotation);
        }

        [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity", Justification = "ToDo - Needs refactoring")]
        private void SetEncodingProperties(CaptureUse captureUse)
        {
            if (this._videoEncodingProfile.Video != null)
            {
                if (captureUse == CaptureUse.Photo)
                {
                    var allProps =
                        this._mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(
                            MediaStreamType.Photo);

                    var photoProps = GetMediaProperties(allProps, CaptureUse.Photo) as ImageEncodingProperties;

                    if (photoProps == null)
                    {
                        var videoProps = GetMediaProperties(allProps, CaptureUse.Video) as VideoEncodingProperties;

                        if (videoProps != null)
                        {
                            this._imageEncodingProperties.Height = videoProps.Height;
                            this._imageEncodingProperties.Width = videoProps.Width;

                            this._mediaEncodingProperties = videoProps;
                        }
                        else
                        {
                            VideoEncodingProperties maxProps = null;

                            var max = 0;

                            foreach (var res in from res in allProps.OfType<VideoEncodingProperties>()
                                                where res.Width * res.Height > max
                                                select res)
                            {
                                max = (int)(res.Width * res.Height);
                                maxProps = res;
                            }

                            if (maxProps != null)
                            {
                                this._imageEncodingProperties.Height = maxProps.Height;
                                this._imageEncodingProperties.Width = maxProps.Width;

                                this._mediaEncodingProperties = maxProps;
                            }
                        }
                    }
                    else
                    {
                        this._imageEncodingProperties = photoProps;
                        this._mediaEncodingProperties = this._imageEncodingProperties;
                    }
                }
                else
                {
                    var mediaStreamProps =
                        this._mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(
                            MediaStreamType.VideoPreview).ToList();

                    var videoProps =
                        mediaStreamProps.OfType<VideoEncodingProperties>()
                            .FirstOrDefault(
                                x =>
                                x != null && this._supportedVideoFormats.Contains(x.Subtype.ToLower())
                                && x.Width == this._videoEncodingProfile.Video.Width
                                && x.Height == this._videoEncodingProfile.Video.Height);

                    if (videoProps != null)
                    {
                        this._mediaEncodingProperties = videoProps;
                    }
                    else
                    {
                        var allVideoProps =
                            mediaStreamProps.OfType<VideoEncodingProperties>()
                                .Where(
                                    x =>
                                    x != null && this._supportedVideoFormats.Contains(x.Subtype.ToLower())
                                    && x.Width <= this._videoEncodingProfile.Video.Width
                                    && x.Height <= this._videoEncodingProfile.Video.Height);

                        VideoEncodingProperties maxVideoPropsRatio = null;

                        var max = 0;

                        foreach (var res in from res in allVideoProps
                                            where res.Width * res.Height > max
                                            let ratio = (double)res.Width / res.Height
                                            where ratio > 1.34
                                            select res)
                        {
                            max = (int)(res.Width * res.Height);
                            maxVideoPropsRatio = res;
                        }

                        if (maxVideoPropsRatio != null)
                        {
                            this._mediaEncodingProperties = maxVideoPropsRatio;
                        }
                        else
                        {
                            VideoEncodingProperties maxVideoProps = null;

                            max = 0;

                            foreach (var res in from res in allVideoProps
                                                where res.Width * res.Height > max
                                                select res)
                            {
                                max = (int)(res.Width * res.Height);
                                maxVideoProps = res;
                            }

                            if (maxVideoProps != null)
                            {
                                this._mediaEncodingProperties = maxVideoProps;
                            }
                        }
                    }
                }
            }
        }

        private static IMediaEncodingProperties GetMediaProperties(IEnumerable<IMediaEncodingProperties> allProps, CaptureUse captureUse)
        {
            var max = 0;

            if (captureUse == CaptureUse.Photo)
            {
                ImageEncodingProperties photoProps = null;

                foreach (var res in from res in allProps.OfType<ImageEncodingProperties>()
                                    where res.Width * res.Height > max
                                    let ratio = (double)res.Width / res.Height
                                    where ratio > 1.34
                                    select res)
                {
                    max = (int)(res.Width * res.Height);
                    photoProps = res;
                }

                return photoProps;
            }

            VideoEncodingProperties videoProps = null;

            foreach (var res in from res in allProps.OfType<VideoEncodingProperties>()
                                where res.Width * res.Height > max
                                let ratio = (double)res.Width / res.Height
                                where ratio > 1.34
                                select res)
            {
                max = (int)(res.Width * res.Height);
                videoProps = res;
            }

            return videoProps;
        }


        /// <summary>
        /// Captures the preview of the <see cref="MediaCapture"/> element as a photo to a <see cref="StorageFile"/>.
        /// </summary>
        /// <returns>
        /// The <see cref="StorageFile"/>.
        /// </returns>
        public async Task<StorageFile> CapturePhoto()
        {
            var file = await CreateFile(".jpg");

            if (this._mediaCapture != null)
            {
                try
                {
                    await this._mediaCapture.CapturePhotoToStorageFileAsync(this._imageEncodingProperties, file);
                }
                catch (Exception)
                {
                    if (file == null)
                    {
                        return null;
                    }
                }

                return file;
            }

            return null;
        }

        /// <summary>
        /// Saves the cropped image from the ImageCropUI.
        /// </summary>
        /// <param name="image">
        /// The cropped image to save.
        /// </param>
        /// <returns>
        /// The <see cref="StorageFile"/>.
        /// </returns>
        public async Task<StorageFile> SaveCroppedImage(WriteableBitmap image)
        {
            if (image != null)
            {
                var file = await CreateFile(".jpg");
                var bitmapEncoderGuid = BitmapEncoder.JpegEncoderId;

                using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var encoder = await BitmapEncoder.CreateAsync(bitmapEncoderGuid, stream);

                    var pixelStream = image.PixelBuffer.AsStream();
                    var pixels = new byte[pixelStream.Length];
                    await pixelStream.ReadAsync(pixels, 0, pixels.Length);

                    encoder.SetPixelData(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Ignore,
                        (uint)image.PixelWidth,
                        (uint)image.PixelHeight,
                        96.0,
                        96.0,
                        pixels);

                    //encoder.BitmapTransform.Rotation = rotation;

                    await encoder.FlushAsync();
                }

                return file;
            }
            return null;
        }

        /// <summary>
        /// Saves and rotates an image.
        /// </summary>
        /// <param name="image">
        /// The image to rotate.
        /// </param>
        /// <param name="rotation">
        /// The rotation.
        /// </param>
        /// <returns>
        /// The <see cref="StorageFile"/>.
        /// </returns>
        public async Task<StorageFile> SaveAndRotateImage(WriteableBitmap image, BitmapRotation rotation)
        {
            var file = await this.SaveCroppedImage(image);

            var rotated = await this.RotateImage(rotation, file);

            return rotated;
        }

        private async Task<StorageFile> RotateImage(int rotation, IStorageFile file)
        {
            var data = await FileIO.ReadBufferAsync(file);

            var ms = new InMemoryRandomAccessStream();
            var dw = new DataWriter(ms);
            dw.WriteBuffer(data);
            await dw.StoreAsync();
            ms.Seek(0);

            var bm = new BitmapImage();
            await bm.SetSourceAsync(ms);

            var wb = new WriteableBitmap(bm.PixelHeight, bm.PixelWidth);
            ms.Seek(0);

            await wb.SetSourceAsync(ms);
            var rotated = wb.Rotate(rotation);

            var result = await this.SaveCroppedImage(rotated);

            return result;
        }

        private async Task<StorageFile> RotateImage(BitmapRotation rotation, IStorageFile file)
        {
            int wbRotate = 0;

            switch (rotation)
            {
                case BitmapRotation.Clockwise90Degrees:
                    wbRotate = 90;
                    break;
                case BitmapRotation.Clockwise180Degrees:
                    wbRotate = 180;
                    break;
                case BitmapRotation.Clockwise270Degrees:
                    wbRotate = 270;
                    break;
            }

            var result = await this.RotateImage(wbRotate, file);

            return result;
        }

        /// <summary>
        /// Records the preview of the <see cref="MediaCapture"/> element as a video to a <see cref="StorageFile"/>.
        /// </summary>
        /// <returns>
        /// The <see cref="StorageFile"/>.
        /// </returns>
        public async Task<StorageFile> StartVideoRecording()
        {
            var rotation = this.GetVideoRotation(DisplayInformation.GetForCurrentView().CurrentOrientation);
            this.SetRecordOrientation(rotation);

            var file = await CreateFile(".mp4");

            if (this._mediaCapture != null)
            {
                await this._mediaCapture.StartRecordToStorageFileAsync(this._videoEncodingProfile, file);
            }

            this.IsRecording = true;
            return file;
        }

        /// <summary>
        /// Stops the recording of the preview of the <see cref="MediaCapture"/> element.
        /// </summary>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public async Task StopVideoRecording()
        {
            if (this._mediaCapture != null)
            {
                if (this.IsRecording)
                {
                    await this._mediaCapture.StopRecordAsync();
                }
            }

            this.IsRecording = false;
        }

        /// <summary>
        /// Swaps between the current active camera and the secondary.
        /// </summary>
        /// <param name="primaryUse">
        /// The primary use for the camera.
        /// </param>
        /// <param name="videoQuality">
        /// The video quality (for recording only).
        /// </param>
        /// <returns>
        /// The <see cref="MediaCapture"/>.
        /// </returns>
        public async Task<MediaCapture> ChangeCamera(
            CaptureUse primaryUse,
            VideoEncodingQuality videoQuality)
        {
            switch (this.ActiveCamera)
            {
                case Panel.Front:
                    this.ActiveCamera = Panel.Back;
                    return await this.Initialize(primaryUse, videoQuality);
                case Panel.Back:
                    this.ActiveCamera = Panel.Front;
                    return await this.Initialize(primaryUse, videoQuality);
            }

            return null;
        }

        /// <summary>
        /// Starts the preview of the <see cref="MediaCapture"/> element.
        /// </summary>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public async Task StartPreview()
        {
            if (!this._isPreviewing)
            {
                await this._mediaCapture.StartPreviewAsync();

                this._isPreviewing = true;
            }
        }

        /// <summary>
        /// Stops the preview of the <see cref="MediaCapture"/> element.
        /// </summary>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public async Task StopPreview()
        {
            if (this._isPreviewing)
            {
                await this._mediaCapture.StopPreviewAsync();

                this._isPreviewing = false;
            }
        }

        /// <summary>
        /// Changes the current <see cref="VideoRotation"/> based on the devices current <see cref="DisplayOrientations"/>.
        /// </summary>
        /// <param name="orientation">
        /// The orientation of the device.
        /// </param>
        public void ChangeOrientation(DisplayOrientations orientation)
        {
            if (this._mediaCapture != null)
            {
                var rotation = this.GetVideoRotation(orientation);
                this._mediaCapture.SetPreviewRotation(rotation);
                this._mediaCapture.SetRecordRotation(rotation);
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        /// <filterpriority>2</filterpriority>
        public async void Dispose()
        {
            if (this._mediaCapture != null)
            {
                if (!this._isDisposing)
                {
                    this._isDisposing = true;

                    await this.StopVideoRecording();

                    await this.StopPreview();

                    if (!this._isPreviewing)
                    {
                        this._mediaCapture.Failed -= this.OnMediaCaptureFailed;
                        this._mediaCapture.Dispose();
                        this._mediaCapture = null;

                        this._isDisposing = false;
                    }
                }
            }
        }

        private VideoRotation GetVideoRotation(DisplayOrientations orientations)
        {
            switch (orientations)
            {
                case DisplayOrientations.Landscape:
                    return VideoRotation.None;
                case DisplayOrientations.PortraitFlipped:
                    if (this.ActiveCamera == Panel.Back)
                    {
                        return VideoRotation.Clockwise270Degrees;
                    }
                    else
                    {
                        return VideoRotation.Clockwise90Degrees;
                    }

                case DisplayOrientations.Portrait:
                    if (this.ActiveCamera == Panel.Back)
                    {
                        return VideoRotation.Clockwise90Degrees;
                    }
                    else
                    {
                        return VideoRotation.Clockwise270Degrees;
                    }

                case DisplayOrientations.LandscapeFlipped:
                    return VideoRotation.Clockwise180Degrees;
            }
            return VideoRotation.None;
        }

        private void OnMediaCaptureFailed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs)
        {
            this._mediaCapture.Failed -= this.OnMediaCaptureFailed;
            this._mediaCapture.Dispose();
            this._mediaCapture = null;
        }

        private static async Task<StorageFile> CreateFile(string extension)
        {
            var folder = ApplicationData.Current.TemporaryFolder;
            return await folder.CreateFileAsync(string.Format("{0}{1}", Guid.NewGuid(), extension));
        }

        private async Task<DeviceInformation> GetCamera(Panel desiredPanel)
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

            this.IsFrontFacingAvailable = CheckFrontFacingCameraExists(devices);

            var camera =
                devices.FirstOrDefault(
                    info => info.EnclosureLocation != null && info.EnclosureLocation.Panel == desiredPanel);



            if (camera != null)
            {
                return camera;
            }
            if (devices.Count > 0) return devices[0];

            throw new InvalidOperationException(string.Format("Camera of type {0} doesn't exist.", desiredPanel));
        }

        private static bool CheckFrontFacingCameraExists(IEnumerable<DeviceInformation> enclosures)
        {
            var frontCamera =
                enclosures.FirstOrDefault(x => x.EnclosureLocation != null && x.EnclosureLocation.Panel == Panel.Front);

            return frontCamera != null;
        }
    }
}
