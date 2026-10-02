using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media.Imaging;
using Proofly.Core;

namespace Proofly.Models
{
    public enum ShotKind
    {
        Image,
        Video,
    }

    /// <summary>
    /// One item in the session. The screenshot or recording itself is a file in
    /// the session folder. Only the small preview is held in memory, which keeps
    /// a long session light.
    /// </summary>
    public sealed class Shot : INotifyPropertyChanged
    {
        private string _name;
        private BitmapSource _thumb;
        private int _width;
        private int _height;
        private long _size;
        private long _durationMs;
        private int _order;
        private int _dropHint;
        private bool _isDragging;
        private bool _isHighlighted;

        public Shot()
        {
            Id = Guid.NewGuid().ToString("N");
        }

        public string Id { get; set; }

        public ShotKind Kind { get; set; }

        /// <summary>Full path of the PNG or MP4 file.</summary>
        public string FilePath { get; set; }

        /// <summary>Full path of the stored preview, empty when there is none.</summary>
        public string ThumbPath { get; set; }

        public bool HasAudio { get; set; }

        public bool IsVideo
        {
            get { return Kind == ShotKind.Video; }
        }

        public string Name
        {
            get { return _name; }
            set { _name = value; Raise("Name"); }
        }

        public BitmapSource Thumb
        {
            get { return _thumb; }
            set { _thumb = value; Raise("Thumb"); }
        }

        public int Width
        {
            get { return _width; }
            set { _width = value; Raise("Width"); Raise("Sub"); }
        }

        public int Height
        {
            get { return _height; }
            set { _height = value; Raise("Height"); Raise("Sub"); }
        }

        public long Size
        {
            get { return _size; }
            set { _size = value; Raise("Size"); Raise("Sub"); }
        }

        public long DurationMs
        {
            get { return _durationMs; }
            set { _durationMs = value; Raise("DurationMs"); Raise("Badge"); }
        }

        /// <summary>
        /// Page the screenshot lands on in the exported document, counted from
        /// one. Zero for recordings, which are saved as separate files.
        /// </summary>
        public int Order
        {
            get { return _order; }
            set
            {
                if (_order == value) return;
                _order = value;
                Raise("Order");
                Raise("OrderText");
                Raise("HasOrder");
            }
        }

        public string OrderText
        {
            get { return _order.ToString(CultureInfo.InvariantCulture); }
        }

        public bool HasOrder
        {
            get { return _order > 0; }
        }

        /// <summary>
        /// Where a dragged card would land relative to this one: -1 before it,
        /// 1 after it, 0 when nothing is being dragged over it.
        /// </summary>
        public int DropHint
        {
            get { return _dropHint; }
            set
            {
                if (_dropHint == value) return;
                _dropHint = value;
                Raise("DropHint");
            }
        }

        /// <summary>True while this card is being dragged to a new place.</summary>
        public bool IsDragging
        {
            get { return _isDragging; }
            set
            {
                if (_isDragging == value) return;
                _isDragging = value;
                Raise("IsDragging");
            }
        }

        /// <summary>Marks the card for a moment after it was moved, so the change is easy to spot.</summary>
        public bool IsHighlighted
        {
            get { return _isHighlighted; }
            set
            {
                if (_isHighlighted == value) return;
                _isHighlighted = value;
                Raise("IsHighlighted");
            }
        }

        /// <summary>Second line of a gallery card.</summary>
        public string Sub
        {
            get { return _width + " x " + _height + " - " + Files.FormatSize(_size); }
        }

        /// <summary>Length shown on the thumbnail of a recording.</summary>
        public string Badge
        {
            get { return Files.FormatDuration(_durationMs); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string property)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(property));
        }
    }
}
