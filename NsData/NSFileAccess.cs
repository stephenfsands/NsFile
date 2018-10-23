using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace NSFileAccess
{
    public class NsFile
    {
        public enum FileTypes
        {
            Average,
            Epoched,
            Continuous,
            Coherence,
            Unknown
        }

        public enum GenderType
        {
            SexMale,
            SexFemale,
            SexUnspecified
        }

        public enum HandPreference
        {
            HandR,
            HandL,
            HandMixed,
            HandUnspecified
        }

        private readonly object _get;
        private readonly ulong[] _id = new ulong[2];
        private readonly ulong[] _setupTagMainId = {0x805316f08a0502ce, 0x8a110b20a901f4f7};
        private string _asciizId = "NSI TFF";
        private Basic _basic;
        private BinaryReader _binRead;
        private BinaryWriter _binWrite;
        private bool _dataAvailable;
        private long _dataPosition;
        private readonly List<float> _dataValues = new List<float>();
        private long _EndOfHeaderPosition = 0;
        private Epoch _epoch;
        private Event2 _Event;
        private readonly Event1 _event1 = new Event1();
        private uint _eventCount;
        private readonly List<Event2> _eventTable = new List<Event2>();

        /// <summary>
        ///     Local variables and functions
        /// </summary>
        private long _fileSize;

        private FrequencyStruct _frequency;
        private Fsp _fsp;
        private TagMainChunk _mainChunk;
        private TagSubChunk _mainSubChunk;
        private readonly List<ElectrodeStructure> _mElectrode = new List<ElectrodeStructure>();
        private Occular _occular;
        private readonly List<NsElectrodeStruct> _scanElectrodeV3 = new List<NsElectrodeStruct>();
        private NsHeaderStruct _scanHeader;
        private SubjectInfo _subject;
        private TagId _TagId = new TagId();
        private Teeg _teeg;
        private Trigger _trigger;
        private readonly List<float> _varianceValues = new List<float>();

        /// <summary>
        ///     Version returns a string Identifying the current file version such as Version 3.0
        /// </summary>
        public string Version
        {
            get => _scanHeader.rev;
            set => _scanHeader.rev = value;
        }

        /// <summary>
        ///     Subject ID string
        /// </summary>
        protected string Id
        {
            get => _scanHeader.id;
            set => _scanHeader.id = value;
        }

        /// <summary>
        ///     Operator name string
        /// </summary>
        public string Oper
        {
            get => _scanHeader.oper;
            set => _scanHeader.oper = value;
        }

        /// <summary>
        ///     Doctor ordering test string
        /// </summary>
        protected string Doctor
        {
            get => _scanHeader.doctor;
            set => _scanHeader.doctor = value;
        }

        /// <summary>
        ///     Medications string
        /// </summary>
        public string Med
        {
            get => _scanHeader.med;
            set => _scanHeader.med = value;
        }

        /// <summary>
        ///     Hospital/Institution string
        /// </summary>
        public string Hospital
        {
            get => _scanHeader.hospital;
            set => _scanHeader.hospital = value;
        }

        /// <summary>
        ///     Categorization string
        /// </summary>
        public string Category
        {
            get => _scanHeader.category;
            set => _scanHeader.category = value;
        }

        /// <summary>
        ///     Patient/Subject string
        /// </summary>
        protected string Patient
        {
            get => _scanHeader.patient;
            set => _scanHeader.patient = value;
        }

        /// <summary>
        ///     State of wakefulness string
        /// </summary>
        public string State
        {
            get => _scanHeader.state;
            set => _scanHeader.state = value;
        }

        /// <summary>
        ///     Label for experiment string
        /// </summary>
        public string Label
        {
            get => _scanHeader.label;
            set => _scanHeader.label = value;
        }

        /// <summary>
        ///     Date of recording string
        /// </summary>
        protected string Date
        {
            get => _scanHeader.date;
            set => _scanHeader.date = value;
        }

        /// <summary>
        ///     Time of recording string
        /// </summary>
        protected string Time
        {
            get => _scanHeader.time;
            set
            {
                _asciizId = value;
                _scanHeader.label = Time;
            }
        }

        /// <summary>
        ///     Comparison file
        /// </summary>
        public string Compfile
        {
            get => _scanHeader.compfile;
            set
            {
                _asciizId = value ?? throw new ArgumentNullException(nameof(value));
                _scanHeader.label = Compfile;
            }
        }

        /// <summary>
        ///     Event sorting file
        /// </summary>
        public string Sortfile
        {
            get => _scanHeader.sortfile;
            set
            {
                _asciizId = value;
                _scanHeader.label = Sortfile;
            }
        }

        /// <summary>
        ///     Referring doctor name
        /// </summary>
        public string Refname
        {
            get => _scanHeader.refname;
            set => _scanHeader.refname = value;
        }

        /// <summary>
        ///     Overlay image screen name
        /// </summary>
        public string Screen
        {
            get => _scanHeader.screen;
            set => _scanHeader.screen = value;
        }

        /// <summary>
        ///     Associated task file
        /// </summary>
        public string Taskfile
        {
            get => _scanHeader.taskfile;
            set => _scanHeader.taskfile = value;
        }

        /// <summary>
        ///     Associated sequence file
        /// </summary>
        public string Seqfile
        {
            get => _scanHeader.seqfile;
            set => _scanHeader.seqfile = value;
        }

        /// <summary>
        ///     Associated Event File
        /// </summary>
        public string EventFile
        {
            get => _scanHeader.EventFile;
            set => _scanHeader.EventFile = value;
        }

        /// <summary>
        ///     Offset in bytes to next file in linked files
        /// </summary>
        public uint NextFile
        {
            get => _scanHeader.NextFile;
            set => _scanHeader.NextFile = value;
        }

        /// <summary>
        ///     Offset in bytes to previous file
        /// </summary>
        public uint PrevFile
        {
            get => _scanHeader.PrevFile;
            set => _scanHeader.PrevFile = value;
        }

        /// <summary>
        ///     return known/unknown  file type
        /// </summary>
        public FileTypes FileType
        {
            get
            {
                // Check immediately for coherence file
                if (_scanHeader.CoherenceFlag == 1) return FileTypes.Coherence;

                if (_scanHeader.savemode == (byte) NsDefinitions.ContinuousEegMode) return FileTypes.Continuous;

                if (_scanHeader.domain == (byte) NsDefinitions.TimeDomain &&
                    _scanHeader.savemode != (byte) NsDefinitions.FastSinglePoint)
                {
                    if (_scanHeader.type == (byte) NsDefinitions.AveragedFileType
                        || _scanHeader.type == (byte) NsDefinitions.GroupAverage
                        || _scanHeader.type == (byte) NsDefinitions.ComparisonAverage
                        || _scanHeader.type == (byte) NsDefinitions.GroupComparsion)
                        return FileTypes.Average;
                    return FileTypes.Unknown;
                }

                return FileTypes.Unknown;
            }
            set => _scanHeader.type = (byte) value;
        }

        /// <summary>
        ///     Channel List
        /// </summary>
        public List<string> ChannelNames
        {
            get
            {
                var label = new List<string>();
                for (var i = 0; i < NumberOfChannels; i++) label.Add(_scanElectrodeV3[i].lab);
                return label;
            }
        }

        /// <summary>
        ///     Number of Channels in recording
        /// </summary>
        protected uint NumberOfChannels
        {
            get => _scanHeader.nchannels;
            set
            {
                if (_scanElectrodeV3.Count > 0) return;
                var electrode = new NsElectrodeStruct();
                if (value <= 0) return;
                _scanHeader.nchannels = (ushort) value;
                for (var i = 0; i < _scanHeader.nchannels; ++i) _scanElectrodeV3.Add(electrode);
            }
        }

        /// <summary>
        ///     Number of sweeps in average
        /// </summary>
        public uint NumberOfSweeps
        {
            get => _scanHeader.nsweeps;
            set => _scanHeader.nsweeps = (ushort) value;
        }

        /// <summary>
        ///     Number of Sweeps
        /// </summary>
        private uint NumberOfPoints
        {
            get => _scanHeader.pnts;
            set => _scanHeader.pnts = (ushort) value;
        }

        /// <summary>
        ///     Average age of in group
        /// </summary>
        public float AverageAge
        {
            get => _scanHeader.mean_age;
            set => _scanHeader.mean_age = value;
        }

        /// <summary>
        ///     Age of current subject
        /// </summary>
        public uint Age
        {
            get => _scanHeader.age;
            set => _scanHeader.age = (ushort) value;
        }

        /// <summary>
        ///     Gender of subject
        /// </summary>
        public string Gender
        {
            get
            {
                if (_scanHeader.sex == 'M')
                {
                    var gender = "Male";
                    return gender;
                }
                else
                {
                    var gender = "Female";
                    return gender;
                }
            }
            set
            {
                var gender = value;
                _scanHeader.sex = gender == "Male" ? 'M' : 'F';
            }
        }

        /// <summary>
        ///     Handedness of subject
        /// </summary>
        public string Handedness
        {
            get
            {
                switch (_scanHeader.hand)
                {
                    case 'L':
                        return "Left";
                    case 'M':
                        return "Mixed";
                    default:
                        return "Right";
                }
            }
            set
            {
                var handedness = value;
                switch (handedness)
                {
                    case "Left":
                        _scanHeader.hand = 'L';
                        break;
                    case "Mixed":
                        _scanHeader.hand = 'M';
                        break;
                    default:
                        _scanHeader.hand = 'R';
                        break;
                }
            }
        }

        /// <summary>
        ///     Average Accuracy of subject on a task
        /// </summary>
        public float MeanAccuracy
        {
            get => _scanHeader.MeanAccuracy;
            set => _scanHeader.MeanAccuracy = value;
        }

        /// <summary>
        ///     Average response Latency of subject on a task
        /// </summary>
        public float MeanLatency
        {
            get => _scanHeader.MeanLatency;
            set => _scanHeader.MeanLatency = value;
        }

        /// <summary>
        ///     Number of events in event table
        /// </summary>
        public uint NumEvents
        {
            get => (uint) _scanHeader.NumEvents;
            set => _scanHeader.NumEvents = (int) value;
        }

        /// <summary>
        ///     Frequency of average display update
        /// </summary>
        public uint AverageDisplayUpdateRate
        {
            get => _scanHeader.avgupdate;
            set => _scanHeader.avgupdate = (ushort) value;
        }

        /// <summary>
        ///     The type of averaging used during acquisition/post-processing
        /// </summary>
        public byte AverageMode
        {
            get => _scanHeader.avgmode;
            set => _scanHeader.avgmode = value;
        }

        /// <summary>
        ///     Expected number of sweeps in average
        /// </summary>
        public uint NumExpectedSweeps
        {
            get => _scanHeader.nsweeps;
            set => _scanHeader.nsweeps = (ushort) value;
        }

        /// <summary>
        ///     Number of completed sweeps in average
        /// </summary>
        public uint NumCompletedSweeps
        {
            get => _scanHeader.compsweeps;
            set => _scanHeader.compsweeps = (ushort) value;
        }

        /// <summary>
        ///     Number of acceptable sweeps in average
        /// </summary>
        public uint NumAcceptedSweeps
        {
            get => _scanHeader.acceptcnt;
            set => _scanHeader.acceptcnt = (ushort) value;
        }

        /// <summary>
        ///     Number of rejected sweeps in average
        /// </summary>
        public uint NumRejectedSweeps
        {
            get => _scanHeader.rejectcnt;
            set => _scanHeader.rejectcnt = (ushort) value;
        }

        /// <summary>
        ///     Acquisition domain TIME=0, FREQ=1
        /// </summary>
        public bool FrequencyDomain
        {
            get => Convert.ToBoolean(_scanHeader.domain);
            set => _scanHeader.domain = Convert.ToByte(value);
        }

        /// <summary>
        ///     Indicates that variance is present for average
        /// </summary>
        private bool VariancePresent
        {
            get => Convert.ToBoolean(_scanHeader.variance);
            set => _scanHeader.variance = Convert.ToByte(value);
        }

        /// <summary>
        ///     Analog-to-Digital conversion rate
        /// </summary>
        protected uint AtodRate
        {
            get => _scanHeader.rate;
            set => _scanHeader.rate = (ushort) value;
        }

        /// <summary>
        ///     Scale factor overall replaced by individual channel calibration
        /// </summary>
        protected double CalibrationScaleFactor
        {
            get => _scanHeader.scale;
            set => _scanHeader.scale = value;
        }

        /// <summary>
        ///     VEOG corrected flag
        /// </summary>
        public bool VeogHasBeenCorrected
        {
            get => Convert.ToBoolean(_scanHeader.veogcorrect);
            set => _scanHeader.veogcorrect = Convert.ToByte(value);
        }

        /// <summary>
        ///     HEOG corrected flag
        /// </summary>
        public bool HeogHasBeenCorrected
        {
            get => Convert.ToBoolean(_scanHeader.heogcorrect);
            set => _scanHeader.heogcorrect = Convert.ToByte(value);
        }

        /// <summary>
        ///     AUX1 corrected flag
        /// </summary>
        public bool Aux1HasBeenCorrected
        {
            get => Convert.ToBoolean(_scanHeader.aux1correct);
            set => _scanHeader.aux1correct = Convert.ToByte(value);
        }

        /// <summary>
        ///     AUX2 corrected flag
        /// </summary>
        public bool Aux2HasBeenCorrected
        {
            get => Convert.ToBoolean(_scanHeader.aux2correct);
            set => _scanHeader.aux2correct = Convert.ToByte(value);
        }

        /// <summary>
        ///     Number of samples in continuous file
        /// </summary>
        private ulong NumContinuousSamples
        {
            get => _scanHeader.NumSamples;
            set => _scanHeader.NumSamples = (uint) value;
        }

        /// <summary>
        ///     Position of event table in bytes
        /// </summary>
        private ulong EventTablePosition
        {
            get => _scanHeader.EventTablePos;
            set => _scanHeader.EventTablePos = (uint) value;
        }

        /// <summary>
        ///     Number of seconds to displayed per page
        /// </summary>
        public float NumSecondsPerDisplayPage
        {
            get => _scanHeader.continuousSeconds;
            set => _scanHeader.continuousSeconds = value;
        }

        /// <summary>
        ///     Display X Minimum
        /// </summary>
        public double DisplayXMin
        {
            get => _scanHeader.DisplayXmin;
            set
            {
                _scanHeader.xmin = (float) value;
                _scanHeader.DisplayXmin = (float) value;
            }
        }

        /// <summary>
        ///     Display X Maximum
        /// </summary>
        /// ummary>
        public double DisplayXMax
        {
            get => _scanHeader.DisplayXmax;
            set
            {
                _scanHeader.xmax = (float) value;
                _scanHeader.DisplayXmax = (float) value;
            }
        }

        public uint ParallelPortAddress
        {
            get => _scanHeader.port;
            set => _scanHeader.port = (ushort) value;
        }

        /// <summary>
        ///     byte  continuousType
        ///     3.2:    0 = original type, with interleaved stimulus and response channels
        ///     1 = multiplexed data with event table
        ///     2 = DCMES
        ///     3 = original SynAmps (block multiplexed)
        /// </summary>
        public byte ContinuousFileType
        {
            get => _scanHeader.continuousType;
            set => _scanHeader.continuousType = value;
        }

        /// <summary>
        ///     Flag for amplifier to perform an auto-correction for DC
        /// </summary>
        public bool AutoCorrectDcLevelFlag
        {
            get => Convert.ToBoolean(_scanHeader.AutoCorrectFlag);
            set => _scanHeader.AutoCorrectFlag = Convert.ToByte(value);
        }

        private int TagIdInvalid { get; } = 0;

        public void ReadEvents()
        {
            var pos = (long) EventTablePosition;
            _binRead.BaseStream.Seek(pos, SeekOrigin.Begin); //go to start of event table

            var length = Marshal.SizeOf(_teeg);
            byte[] readBuffer;
            try
            {
                readBuffer = _binRead.ReadBytes(length);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
            _teeg = (Teeg) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Teeg));
            var eventSize = (uint) Marshal.SizeOf(_Event);

            var unused = (uint) Marshal.SizeOf(_event1);

            _eventCount = (uint) _teeg.Size / eventSize;
            handle.Free();
            var i = 0;
            for (; i < _eventCount; ++i) ReadNextEvent();
        }

        private void ReadNextEvent()
        {
            var length = Marshal.SizeOf(_Event);
            byte[] readBuffer;
            try
            {
                readBuffer = _binRead.ReadBytes(length);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
            _Event = (Event2) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Event2));
            handle.Free();

            _eventTable.Add(_Event);
        }

        public uint GetEventCount()
        {
            return _eventCount;
        }

        private void WriteEvents()
        {
            var writeBuffer = StructureToByteArray(_teeg);
            try
            {
                _binWrite.Write(writeBuffer);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var event1Size = (uint) Marshal.SizeOf(_event1);
            for (var i = 0; i < _eventCount; ++i) WriteNextEvent(i);
        }

        private void WriteNextEvent(int index)
        {
            _Event = _eventTable.ElementAt(index);
            var writeBuffer = StructureToByteArray(_Event);
            try
            {
                _binWrite.Write(writeBuffer);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }
        }

        public void LoadFile(string filename)
        {
            var f = new FileInfo(filename);
            _fileSize = f.Length;
            try
            {
                Stream sr = f.Open(FileMode.Open, FileAccess.Read);
                _binRead = new BinaryReader(sr);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var length = Marshal.SizeOf(_scanHeader);
            byte[] readBuffer;
            try
            {
                readBuffer = _binRead.ReadBytes(length);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
            _scanHeader = (NsHeaderStruct) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(NsHeaderStruct));
            handle.Free();
            _scanElectrodeV3.Clear();

            for (var i = 0; i < NumberOfChannels; i++) ReadElectrode();
            if (GetFileType() == FileTypes.Continuous)
            {
                ReadCntData();
                ReadEvents();
            }
            else if (GetFileType() == FileTypes.Average)
            {
                ReadAvgData();
            }

            GetBasic();
            GetElectrodes();
            GetEpoch();
            GetTrigger();
            GetFsp();
            GetFrequency();
            GetOccular();
            GetSubject();
        }

        public void ReadClose()
        {
            _binRead.Close();
        }

        public bool WriteData(string filename)
        {
            var f = new FileInfo(filename);
            Stream srw = f.Create();
            _binWrite = new BinaryWriter(srw);
            var writeBuffer = StructureToByteArray(_scanHeader);

            try
            {
                _binWrite.Write(writeBuffer);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            for (var i = 0; i < NumberOfChannels; i++) WriteElectrode(i);
            _dataPosition = _binWrite.BaseStream.Position;
            if (FileType == FileTypes.Continuous)
            {
                WriteCntData();
                WriteEvents();
            }
            else if (FileType == FileTypes.Average)
            {
                WriteAvgData();
            }

            Init(_binWrite.BaseStream.Position);
            Set32BitMode(true);
            WriteTeegFileHeader();
            SaveBasic();
            SaveElectrodes();
            SaveEpoch();
            SaveTrigger();
            SaveFsp();
            SaveFrequency();
            SaveOccular();
            SaveSubject();
            _binWrite.Close();
            return true;
        }

        public void ReadCntData()
        {
            try
            {
                _binRead.BaseStream.Seek(GetHeaderSize(), SeekOrigin.Begin);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to seek to start of data. Reason: " + ex.Message);
            }


            var sensitivity = new double[NumberOfChannels];

            if (_scanElectrodeV3 != null && _scanElectrodeV3.Count == 0) return;
            for (var index = 0; index < NumberOfChannels; index++)
            {
                var electrode = (_scanElectrodeV3 ?? throw new InvalidOperationException()).ElementAt(index);
                sensitivity[index] = electrode.sensitivity;
            }

            for (ulong i = 0; i < NumContinuousSamples; i++)
                try
                {
                    var j = 0;
                    for (; j < (int) NumberOfChannels; j++) AddValue(_binRead.ReadInt32());
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to seek to start of data. Reason: " + e.Message);
                    throw;
                }

            _dataAvailable = true;
        }

        public void ReadAvgData()
        {
            try
            {
                _binRead.BaseStream.Seek(GetHeaderSize(), SeekOrigin.Begin);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to seek to start of data. Reason: " + ex.Message);
            }

            if (_scanElectrodeV3.Count == 0) return;

            var channel = 0;
            for (; channel < NumberOfChannels; channel++)
                try
                {
                    _binRead.ReadChars(5);
                    for (var pnt = 0; pnt < NumberOfPoints; pnt++)
                    {
                        var val = _binRead.ReadSingle();
                        AddValue(val);
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to seek to start of data. Reason: " + e.Message);
                    throw;
                }

            if (VariancePresent)
                for (var i = 0; i < NumberOfChannels; i++)
                    try
                    {
                        for (var pnt = 0; pnt < NumberOfPoints; pnt++)
                        {
                            var val = _binRead.ReadSingle();
                            AddVarValue(val);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Failed to seek to start of data. Reason: " + e.Message);
                        throw;
                    }

            _dataAvailable = true;
        }

        protected bool WriteCntData()
        {
            if (!_dataAvailable)
                return false;
            try
            {
                _binWrite.BaseStream.Seek(_dataPosition, SeekOrigin.Begin);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to seek to start of data. Reason: " + ex.Message);
            }

            //double[] sensitivity = new double[numberOfChannels];
            var sensitivity = new double[NumberOfChannels];
            if (_scanElectrodeV3.Count == 0) return false;

            for (var index = 0; index < NumberOfChannels; index++)
            {
                var electrode = _scanElectrodeV3.ElementAt(index);
                sensitivity[index] = electrode.sensitivity;
            }

            for (ulong i = 0; i < NumContinuousSamples; i++)
                try
                {
                    for (var j = 0; j < (int) NumberOfChannels; j++)
                        _binWrite.Write((int) (GetValue(j, (int) i) / 204.8 * sensitivity[j]));
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to seek to start of data. Reason: " + e.Message);
                    throw;
                }

            return true;
        }

        private void WriteAvgData()
        {
            if (!_dataAvailable) return;
            try
            {
                _binWrite.BaseStream.Seek(GetHeaderSize(), SeekOrigin.Begin);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Failed to seek to start of data. Reason: " + ex.Message);
            }

            if (_scanElectrodeV3.Count == 0) return;
            for (var chan = 0; chan < (int) NumberOfChannels; chan++)
                try
                {
                    var label = new byte [5];
                    _binWrite.Write(label);

                    for (var pnt = 0; pnt < NumberOfPoints; pnt++)
                    {
                        var val = GetValue(chan, pnt);
                        _binWrite.Write(val);
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to seek to start of data. Reason: " + e.Message);
                    throw;
                }

            if (!VariancePresent) return;
            {
                var buffer = new byte[5 * NumberOfChannels];
                for (var chan = 0; chan < (int) NumberOfChannels; chan++)
                    try
                    {
                        for (var pnt = 0; pnt < NumberOfPoints; pnt++)
                        {
                            var val = GetVarValue(chan, pnt);
                            _binWrite.Write(val);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Failed to write data. Reason: " + e.Message);
                        throw;
                    }

                _binWrite.Write(
                    buffer); // this is used to correct an error in scan where the label is added but not saved
            }
        }

        private void GetBasic()
        {
            _binRead.BaseStream.Seek(NextFile, SeekOrigin.Begin); //go to start
            SeekToTag((int) SetupTags.Basic, 1);
            var length = Marshal.SizeOf(_basic);
            byte[] readBuffer;
            try
            {
                readBuffer = _binRead.ReadBytes(length);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
            _basic = (Basic) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Basic));
            handle.Free();
        }

        private void GetElectrodes()
        {
            for (var chan = 0; chan < NumberOfChannels; chan++)
            {
                // Read Basic attributes
                _binRead.BaseStream.Seek(NextFile, SeekOrigin.Begin); //go to start
                var electrode = new ElectrodeStructure();
                SeekToTag((int) SetupTags.Electrode + chan, 1);

                var length = Marshal.SizeOf(electrode);
                byte[] readBuffer;
                try
                {
                    readBuffer = _binRead.ReadBytes(length);
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    throw;
                }

                var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
                electrode = (ElectrodeStructure) Marshal.PtrToStructure(handle.AddrOfPinnedObject(),
                    typeof(ElectrodeStructure));
                _mElectrode.Add(electrode);
            }
        }

        private FileTypes GetFileType()
        {
            // Check immediately for coherence file
            if (_scanHeader.CoherenceFlag == 1) return FileTypes.Coherence;

            if (_scanHeader.savemode == (byte) NsDefinitions.ContinuousEegMode) return FileTypes.Continuous;

            if (_scanHeader.domain == (byte) NsDefinitions.TimeDomain &&
                _scanHeader.savemode != (byte) NsDefinitions.FastSinglePoint)
            {
                if (_scanHeader.type == (byte) NsDefinitions.AveragedFileType
                    || _scanHeader.type == (byte) NsDefinitions.GroupAverage
                    || _scanHeader.type == (byte) NsDefinitions.ComparisonAverage
                    || _scanHeader.type == (byte) NsDefinitions.GroupComparsion)
                    return FileTypes.Average;
                return FileTypes.Unknown;
            }

            return FileTypes.Unknown;
        }

        private void SaveBasic()
        {
            SetItem((int) SetupTags.Basic, Marshal.SizeOf(_basic), StructureToByteArray(_basic),
                (int) VersionTags.Basic);
        }

        private void SaveElectrodes()
        {
            for (var i = 0; i < NumberOfChannels; i++)
            {
                var electrode = _mElectrode.ElementAt(i);
                SetItem((int) SetupTags.Electrode + i, Marshal.SizeOf(electrode), StructureToByteArray(electrode),
                    (int) VersionTags.Electrode);
            }
        }

        private void SaveTrigger()
        {
            SetItem((int) SetupTags.Trigger, Marshal.SizeOf(_trigger), StructureToByteArray(_trigger),
                (int) VersionTags.Trigger);
        }

        private void SaveEpoch()
        {
            SetItem((int) SetupTags.Epoch, Marshal.SizeOf(_epoch), StructureToByteArray(_epoch),
                (int) VersionTags.Epoch);
        }

        private void SaveFsp()
        {
            SetItem((int) SetupTags.Fsp, Marshal.SizeOf(_fsp), StructureToByteArray(_fsp), (int) VersionTags.Fsp);
        }

        private void SaveFrequency()
        {
            SetItem((int) SetupTags.Freq, Marshal.SizeOf(_frequency), StructureToByteArray(_frequency),
                (int) VersionTags.Freq);
        }

        private void SaveOccular()
        {
            SetItem((int) SetupTags.Ocular, Marshal.SizeOf(_occular), StructureToByteArray(_occular),
                (int) VersionTags.Ocular);
        }

        private void SaveSubject()
        {
            SetItem((int) SetupTags.Subject, Marshal.SizeOf(_subject), StructureToByteArray(_subject),
                (int) VersionTags.Subject);
        }

        private void GetEpoch()
        {
            _binRead.BaseStream.Seek(NextFile, SeekOrigin.Begin); //go to start
            SeekToTag((int) SetupTags.Epoch, 1);
            var length = Marshal.SizeOf(_epoch);
            byte[] readBuffer;
            try
            {
                readBuffer = _binRead.ReadBytes(length);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
            _epoch = (Epoch) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Epoch));
            handle.Free();
        }

        private void GetTrigger()
        {
            _binRead.BaseStream.Seek(NextFile, SeekOrigin.Begin); //go to start
            SeekToTag((int) SetupTags.Trigger, 1);

            var length = Marshal.SizeOf(_trigger);
            byte[] readBuffer;
            try
            {
                readBuffer = _binRead.ReadBytes(length);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
            _trigger = (Trigger) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Trigger));
            handle.Free();
        }

        private void GetFsp()
        {
            _binRead.BaseStream.Seek(NextFile, SeekOrigin.Begin); //go to start
            SeekToTag((int) SetupTags.Fsp, 1);

            var length = Marshal.SizeOf(_fsp);
            byte[] readBuffer;
            try
            {
                readBuffer = _binRead.ReadBytes(length);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
            _fsp = (Fsp) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Fsp));
            handle.Free();
        }

        private void GetFrequency()
        {
            _binRead.BaseStream.Seek(NextFile, SeekOrigin.Begin); //go to start
            SeekToTag((int) SetupTags.Freq, 1);

            var length = Marshal.SizeOf(_frequency);
            byte[] readBuffer;
            try
            {
                readBuffer = _binRead.ReadBytes(length);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
            _frequency = (FrequencyStruct) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(FrequencyStruct));
            handle.Free();
        }

        private void GetOccular()
        {
            _binRead.BaseStream.Seek(NextFile, SeekOrigin.Begin); //go to start
            SeekToTag((int) SetupTags.Ocular, 1);

            var length = Marshal.SizeOf(_occular);
            byte[] readBuffer;
            try
            {
                readBuffer = _binRead.ReadBytes(length);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
            _occular = (Occular) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(Occular));
            handle.Free();
        }

        private void GetSubject()
        {
            _binRead.BaseStream.Seek(NextFile, SeekOrigin.Begin); //go to start
            SeekToTag((int) SetupTags.Subject, 1);

            var length = Marshal.SizeOf(_subject);
            byte[] readBuffer;
            try
            {
                readBuffer = _binRead.ReadBytes(length);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
            _subject = (SubjectInfo) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(SubjectInfo));
            handle.Free();
        }

        private void ReadElectrode()
        {
            var electrode = new NsElectrodeStruct();
            var length = Marshal.SizeOf(electrode);
            byte[] readBuffer;

            try
            {
                readBuffer = _binRead.ReadBytes(length);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
            electrode = (NsElectrodeStruct) Marshal.PtrToStructure(handle.AddrOfPinnedObject(),
                typeof(NsElectrodeStruct));
            _scanElectrodeV3.Add(electrode);
            handle.Free();
        }

        private void WriteElectrode(int index)
        {
            var electrode = _scanElectrodeV3.ElementAt(index);
            var writeBuffer = StructureToByteArray(electrode);

            try
            {
                _binWrite.Write(writeBuffer);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }
        }

        private static byte[] StructureToByteArray(object obj)
        {
            var len = Marshal.SizeOf(obj);

            var arr = new byte[len];

            var ptr = Marshal.AllocHGlobal(len);

            Marshal.StructureToPtr(obj, ptr, true);

            Marshal.Copy(ptr, arr, 0, len);

            Marshal.FreeHGlobal(ptr);

            return arr;
        }

        public static void ByteArrayToStructure(byte[] bytearray, ref object obj)
        {
            var len = Marshal.SizeOf(obj);

            var i = Marshal.AllocHGlobal(len);

            Marshal.Copy(bytearray, 0, i, len);

            obj = Marshal.PtrToStructure(i, obj.GetType());

            Marshal.FreeHGlobal(i);
        }

        // *********************************** ACCESSORS ************************************************
        /// <summary>
        ///     Computes the size of the header
        /// </summary>
        /// <returns></returns>
        private int GetHeaderSize()
        {
            var electrode = new NsElectrodeStruct();
            var header = new NsHeaderStruct();

            var electrodeSize = Marshal.SizeOf(electrode) * (int) NumberOfChannels;
            var headerSize = Marshal.SizeOf(header);

            headerSize += electrodeSize;
            return headerSize;
        }

        /// <summary>
        ///     Get electrode Calibration factor at index
        /// </summary>
        public float GetElectrodeCalibration(int i)
        {
            return _scanElectrodeV3[i].calib;
        }

        /// <summary>
        ///     Set electrode Calibration factor at index
        /// </summary>
        public void SetElectrodeCalibration(int i, float val)
        {
            var electrode = _scanElectrodeV3[i];
            electrode.calib = val;
            if (i != -1)
                _scanElectrodeV3[i] = electrode;
        }

        /// <summary>
        ///     Set electrode Calibration factor at index
        /// </summary>
        protected void SetElectrodeLabel(int i, string label)
        {
            var electrode = _scanElectrodeV3[i];
            electrode.lab = label;
            if (i < NumberOfChannels)
                _scanElectrodeV3[i] = electrode;
        }

        /// <summary>
        ///     Get electrode sweeps at channel index
        /// </summary>
        public float GetElectrodeSweeps(int i)
        {
            return _scanElectrodeV3[i].n;
        }

        /// <summary>
        ///     Set electrode sweeps at channel index
        /// </summary>
        public void SetElectrodeSweeps(int i, int val)
        {
            var electrode = _scanElectrodeV3[i];
            electrode.n = (ushort) val;
            if (i != -1)
                _scanElectrodeV3[i] = electrode;
        }

        /// <summary>
        ///     Number of averages to make up a group file
        /// </summary>
        public uint GetnumberOfAveragesInGroup()
        {
            return _scanHeader.n;
        }

        /// <summary>
        ///     Number of averages to make up a group file
        /// </summary>
        public void SetnumberOfAveragesInGroup(uint value)
        {
            _scanHeader.n = (ushort) value;
        }

        /// <summary>
        ///     get a point value by channel number
        /// </summary>
        private float GetVarValue(int channel, int point)
        {
            var index = channel * (int) NumberOfPoints + point;
            return _varianceValues[index];
        }

        /// <summary>
        ///     set a point value by channel number
        /// </summary>
        public void SetVarValue(int channel, int point, float val)
        {
            var index = channel * (int) NumberOfPoints + point;
            _varianceValues[index] = val;
        }

        /// <summary>
        ///     add a point value
        /// </summary>
        private void AddVarValue(float val)
        {
            _varianceValues.Add(val);
        }

        private float GetValue(int channel, int point)
        {
            var index = channel * (int) NumberOfPoints + point;

            var val = _dataValues[index] / _scanElectrodeV3[channel].n;
            var calib = _scanElectrodeV3[channel].calib;
            return val * calib;
        }

        /// <summary>
        ///     set a point value by channel number
        /// </summary>
        public void SetValue(int channel, int point, float val)
        {
            var index = channel * (int) NumberOfPoints + point;
            _dataValues[index] = val;
        }

        /// <summary>
        ///     add a point value
        /// </summary>
        private void AddValue(float val)
        {
            _dataValues.Add(val);
        }

        /// <summary>
        ///     insert a point value at channel and point
        /// </summary>
        public void InsertValue(int channel, int point, float val)
        {
            var index = channel * (int) NumberOfPoints + point;
            _dataValues.Insert(index, val);
        }

        private void Init(long offset)
        {
            if (offset <= 0) throw new ArgumentOutOfRangeException(nameof(offset));
            _id[0] = _setupTagMainId[0];
            _id[1] = _setupTagMainId[1];
            _mainChunk.Link = 0;
            _mainChunk.Size = 0;
            _mainChunk.NextEntry = 0;
        }

        private void Set32BitMode(bool bitRes)
        {
            if (bitRes)
                _mainSubChunk.Version |= (ushort) SetupType.Bit32;
            else
                _mainSubChunk.Version |= (ushort) SetupType.Bit16;
        }

        private void SetItem(int id, int size, byte[] byteData, int version)
        {
            if (id == 0) // invalid ID requested?
                return;

            var subChunk = new TagSubChunk
            {
                ID = (uint) id,
                Version = (ushort) version,
                Reserved = 0
            };
            var length = Marshal.SizeOf(subChunk);
            var writeBuffer = new byte[length];
            var handle = GCHandle.Alloc(writeBuffer, GCHandleType.Pinned);
            Marshal.StructureToPtr(subChunk, handle.AddrOfPinnedObject(), true);
            handle.Free();
            try
            {
                _binWrite.Write(writeBuffer, 0, length); // write header
                if (size != 0) _binWrite.Write(byteData, 0, size); // write chunk data, if present
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to set item in TEEG. Reason: " + e.Message);
            }
        }

        public int GetItem(int id, int size, byte[] byteData, int nthOccurence)
        {
            var chunkSize = SeekToTag(id, nthOccurence);

            if (chunkSize == 0) return 0;

            try
            {
                _binRead.Read(byteData, 0, size);
                return size;
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }
        }

        // Gets the next item of specified Id if it exists, returns 0 if not found. This
        // function was added for optimal performance when reading multiple consecutive tags.
        // It is assumed that the current file position points to a sub-chunk header!
        // This function searches for the nth occurrence of the tagid of pTag (1 based)
        // it will leave the file pointer pointing to the beginning of the block following the matched header
        // _MainChunk and _mainSubChunk are set by 'readFileHeader' procedure.
        // If there are no matches (or too few) then the function returns FALSE
        private int SeekToTag(int id, int nOccurrence)
        {
            if (id == 0) return 0;
            try
            {
                if (ReadTeegFileHeader() == 0) return 0;
                var nCount = 0;
                var pos = _binRead.BaseStream.Position;
                var size = _fileSize;
                var length = Marshal.SizeOf(_mainSubChunk);

                while (pos < size)
                {
                    var readBuffer = _binRead.ReadBytes(length);
                    var handle = GCHandle.Alloc(readBuffer, GCHandleType.Pinned);
                    _mainSubChunk =
                        (TagSubChunk) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(TagSubChunk));
                    handle.Free();
                    if (_mainSubChunk.ID == id)
                    {
                        nCount++;
                        pos = _binRead.BaseStream.Position;
                        if (nCount == nOccurrence)
                            return (int) _mainSubChunk.Size;
                    }

                    _binRead.BaseStream.Seek(_mainSubChunk.Size, SeekOrigin.Current);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                throw;
            }

            return 0;
        }

        private long WriteTeegFileHeader()
        {
            var bufferId = new byte[8];
            char[] idTag = {'N', 'S', 'I', ' ', 'T', 'F', 'F'};
            _mainSubChunk.Version = 1;
            for (var i = 0; i < 7; i++) bufferId[i] = (byte) idTag[i];

            try
            {
                try
                {
                    _binWrite.Write(bufferId, 0, 8); // Write file ID string
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to write header in TEEG. Reason: " + e.Message);
                    return 0;
                }

                var writeBuffer = StructureToByteArray(_mainChunk);
                try
                {
                    _binWrite.Write(writeBuffer); // write header
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to write header in TEEG. Reason: " + e.Message);
                    return 0;
                }

                var writeBuffer2 = StructureToByteArray(_mainSubChunk);
                try
                {
                    _binWrite.Write(writeBuffer2); // write header
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Failed to write file header in TEEG. Reason: " + e.Message);
                    return 0;
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine("Failed to write file header in TEEG. Reason: " + e.Message);
                return 0;
            }

            return 8 + Marshal.SizeOf(_mainChunk) + Marshal.SizeOf(_mainSubChunk);
        }

        // Read header, in error return -1, in success return 
        private short ReadTeegFileHeader()
        {
            try
            {
                var buffer = new byte[8];
                char[] idTag = {'N', 'S', 'I', ' ', 'T', 'F', 'F'};
                _binRead.Read(buffer, 0, 8); // Read file ID string
                for (var i = 0; i < 7; i++)
                    if (buffer[i] != idTag[i])
                        return -1;
                // Read main chunk
                var length = Marshal.SizeOf(_mainChunk);
                var readBufferMain = _binRead.ReadBytes(length);
                var handle = GCHandle.Alloc(readBufferMain, GCHandleType.Pinned);
                _mainChunk = (TagMainChunk) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(TagMainChunk));
                handle.Free();

                // Read sub chunk
                length = Marshal.SizeOf(_mainSubChunk);
                var readBufferSub = _binRead.ReadBytes(length);
                handle = GCHandle.Alloc(readBufferSub, GCHandleType.Pinned);
                _mainSubChunk = (TagSubChunk) Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(TagSubChunk));
                handle.Free();

                return (short) _mainSubChunk.Version;
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to write file header in TEEG. Reason: " + e.Message);
                return -1;
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TagMainChunk
        {
            private readonly ulong Id0;
            private readonly ulong Id1;

            public ulong Link; // pointer to the next chunk with this ID

            // note: Link is not currently used, and may become obsolete
            public ulong Size; // size of the chunk
            public ulong NextEntry; // relative position of next entry, can be >= Size
        }

        /// <summary>
        ///     Tag sub chunk
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TagSubChunk
        {
            public uint ID; // ID can start at 1000 (0 - 999 are reserved for internal use of the class "CTaggedFile")
            public ushort Version; // version number for this subchunk ID
            public ushort Reserved; // 2 bytes unused
            public readonly uint Size; // size of chunk
            private readonly uint NextEntry; // relative position of next entry, can be >= Size
        }

        private enum NsDefinitions
        {
            TimeDomain = 0,
            FrquencyDomain = 1,
            ExternTrig = 0,
            InternalTrig = 1,
            ParallelPortTrig = 2,
            PrinterPortTrig = 3,
            SerialCom1Trig = 5,
            SerialCom2Trig = 6,
            Lpt1Trig = 7,
            Lpt2Trig = 8,
            NoTrig = 9,
            ComTrig = 10,
            VoltageRisingEdgeTrig = 1,
            VoltageFallingEdgeTrig = 2,
            AveragedFileType = 0,
            AveragedAvgd = 1,
            EpochedFrequencyDomain = 0,
            EpochedTimeDomain = 1,
            AveragedAndEpochedMode = 2,
            ContinuousEegMode = 3,
            NoStorage = 4,
            ModulationTransferFunction = 5,
            FastSinglePoint = 6,
            CompressSprectralArray = 7,
            GroupAverage = 2,
            ComparisonAverage = 3,
            RelativeChannel = 4,
            GroupComparsion = 5,
            MeanFrequecny = 6,
            RatioTransformed = 1,
            WaveformSubtract = 2,
            TscoreComparisonFile = 3,
            ZscoreComparisonFile = 4
        }

        private enum SetupType
        {
            Bit16 = 0x0000,
            Bit32 = 0x0001,
            Eeg = 0x0000,
            Aep = 0x0010,
            Vep = 0x0020,
            Sep = 0x0040,
            Baep = 0x0080
        }

        /// <summary>
        ///     Version of TAG structures
        /// </summary>
        private enum VersionTags
        {
            MajorVersion42Plus = 4,
            MinorVersion42Plus = 2,
            MinorVerion433Plus = 3,
            Basic = 2,
            Epoch = 2,
            Trigger = 0,
            Fsp = 0,
            Freq = 3,
            Ocular = 0,
            Marker = 0,
            EkgRed = 2,
            EpiRed = 0,
            LineFilter = 0,
            DigitalVideo = 0,
            BlinkRed = 0,
            RefMode = 0,
            HighLevel = 0,
            Electrode = 0,
            Ldr1Electrode = 0,
            Ldr2Electrode = 0,
            Ldr3Electrode = 0,
            Annotation = 0,
            AnnotationDefinition = 0,
            Subject = 0,
            Hotkey = 0,
            WindowOrder = 0,
            FreqBand = 0,
            Snr = 0,
            Ampinfo = 0
        }

        /// <summary>
        ///     TAG numbers for parameter structures
        /// </summary>
        private enum SetupTags
        {
            Basic = 1000,
            Epoch,
            Trigger,
            Fsp,
            Freq = 1005,
            Ocular,
            WindowOrder,
            EkgRed,
            RefMode,
            EpiRed,
            LineFilter,
            Video,
            Electrode = 5000,
            Ldr1Electrode = 15000,
            Ldr2Electrode = 20000,
            Ldr3Electrode = 25000,
            Annotation = 30000,
            AnnotationDefinition = 30001,
            Hotkey = 31000,
            Subject = 40000,
            Marker = 50000,
            DcHeader = 60000,
            DcRecord,
            DcDriftHeader,
            DcDriftCoeff,
            FileVersion = 70000,
            ElectMarker = 80000,
            FreqBand = 90000,
            Snr = 100000,
            HighLevel = 110000,
            AmpInfo = 12000
        }

        /// <summary>
        ///     Maximum values
        /// </summary>
        private enum MaxValues
        {
            Maps = 5,
            DisplayPages = 20,
            AnnotationSize = 256,
            Hilevel = 16,
            Channels = 512
        }

        private enum SnrMethod
        {
            Percentile20,
            UserDefinedWindow,
            BaselineCorrect,
            UserValue
        }

        /// <summary>
        ///     Filter Parameters
        /// </summary>
        private enum FilterTypes
        {
            LowPass = 0,
            HiPass = 1,
            BandPass = 2,
            BandStop = 3,
            ZeroPhaseShift = 0,
            AnalogSimulator = 1
        }

        /// <summary>
        ///     QRS waveform definition
        /// </summary>
        private enum QrsWaveform
        {
            Undefined = -1,
            Std = 0,
            Cor = 1
        }

        /// <summary>
        ///     Annotation parameters
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct AnnotationDefinitions
        {
            public uint Index { get; }

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (int) MaxValues.AnnotationSize)]
            private readonly string annotation;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            private readonly string unused;
        }

        /// <summary>
        ///     Hotkey parameters
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct HotKey
        {
            private readonly uint virtualKey;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = (int) MaxValues.AnnotationSize)]
            public string keyText;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            private readonly string unused;
        }

        /// <summary>
        ///     Amplifier settings
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct AmpInfo
        {
            private readonly float microvoltsPerLSB; // microvolts/Least Significant Bit
            private readonly uint filterType; // Filter algorithm
            private readonly float fowPass; // Hertz
            private readonly uint lowFilterOrder; // slope
            private readonly float highPass; // Hertz
            private readonly uint highFilterOrder; // slope
            private readonly float gain; // Amp Gain //no longer used

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 199)]
            private readonly uint[] reserved; // must be set to 0
        }

        /// <summary>
        ///     Mapping parameters
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MapStruct
        {
            private readonly uint bEnable; // Map enable 
            private readonly uint bFreq; // Freq enable
            private readonly float fDispMin; // Map DispMin
            private readonly float fDispMax; // Map DispMax
            private readonly float fFreqStart; // Map start (frequency domain)
            private readonly float fFreqStop; // Map start 

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            private readonly string label; // Map Label 

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
            private readonly string file; // Map file 

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
            private readonly string ldrfile;

            private readonly uint display; // Attached Display
            private readonly uint dataSourceIndex; // Index to data source
            private readonly float timeStart; // Map start (time domain)
            private readonly float timeStop; // Map start 
            private readonly float rawStart; // Map start (raw)
            private readonly float rawStop; // Map start 	
        }

        /// <summary>
        ///     Filter parameters
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Filter
        {
            private readonly uint enable; // Filter enable
            private readonly float lowPass; // Low pass value
            private readonly float highPass; // High pass value
            private readonly int filterType; // Filter type 0=low pass, 1= high pass 2=band pass 3= band stop
            private readonly uint mode; // Filter mode 0 = zero phase,  1 = analog
            private readonly float notchStart; // Start point of band stop filter
            private readonly float notchStop; // Stop point of band stop filter
            private readonly uint lowPoles; // Number of poles for low pass
            private readonly uint highPoles; // Number of poles for high pass
            private readonly uint notchPoles; // Number of polse for band stop
            private readonly uint rectify; // rectification flag
            private readonly uint allChannels; // 0 all channels selected 1 individual

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            private readonly string unused; // reserved space
        }

        /// <summary>
        ///     3D electrode parameters
        /// </summary>
        private struct Electrodes3D
        {
            private float Xpos; // X position in 3-space, cm
            private float Ypos; // Y position in 3-space, cm
            private float Zpos; // Z position in 3-space, cm
            private byte Available; // is XYZ position info available?

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 19)]
            private string Label; // reserved space
        }

        /// <summary>
        ///     angular position of electrodes in a sphere
        /// </summary>
        private struct AngleSetup
        {
            private float Phi;
            private float Theta;
            private float Radius;
            private uint x;
            private uint y;
        }

        private struct SoundSetup
        {
            private uint enable; //Sound enabled
            private int weight; // Weighting
            private uint left; // Side that sound will appear on

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            private string unused;
        }


        private struct CompareSetup
        {
            private uint color; // Color of waveforms
            private uint index; // index of electrode	
        }


        /// <summary>
        ///     Ekg artifact reduction parameters
        /// </summary>
        private struct EkgReduction
        {
            private uint _bEnable;
            private uint _nAverages;
            private float _fStartTrig;
            private float _fEndTrig;
            private float _fStartArtifact;
            private float _fEndArtifact;
            private float _fThreshold;
            private uint _nDirection;
            private uint _nTrigChan;
            private uint _bArtRej;
            private float _fArtMin;
            private float _fArtMax;
            private uint _bCorrectTrigChan;
            private uint _nTrigType;
            private uint _nExtTrigCode;
            private uint _bUseResponseCode;
            private uint _bBipolar;
            private uint _nBipolarRefChanIndex;
            private uint _bDeMean;
            private float _fRefractoryPeriod;
            private uint _nShiftLimit;
            private uint _nCorrelationThreshold;
            private uint _bEnableCorrelation;
            private uint _bInsertEvents;
            private uint _nCorChan;
            private uint _nQrsMethod;
            private Filter _filterSettings; // filter settings;
            private uint _bDilate;
        }

        /// <summary>
        ///     Line noise filter parameters
        /// </summary>
        private struct LineFilter
        {
            private uint _bEnable;

            private uint _nAverages;

            //	float fStartTrig;
            //	float fEndTrig;
            //	float fStartArtifact;
            //	float fEndArtifact;	
            //	float fThreshold;
            //	uint nDirection;	
            //	uint nTrigChan;
            //	uint bArtRej;
            //	float fArtMin;
            //	float fArtMax;	
            //	uint bCorrectTrigChan;
            //	uint nTrigType;
            //	uint nExtTrigCode;	
            //	uint bUseResponseCode;
            //	uint bBipolar;	
            //	uint nBipolarRefChanIndex;
            //	uint bDeMean;
            //	float fRefractoryPeriod;
            private uint _nShiftLimit;

            //	uint nCorrelationThreshold;
            //	uint bEnableCorrelation;
            //uint bInsertEvents;	
            private uint _nCorChan;
            //	uint nQRSMethod;
            //	SETUP_FILTER FilterSettings;				// filter settings;
        }

        /// <summary>
        /// EPI reduction parameters
        /// </summary>
        /*unsafe public struct epiReduction
        {
            uint  enable;
            uint  windowSize;
            float   trStart;         //int nPreStimEpochPnts;
            float   trEnd;           //int nPostStimEpochPnts;
            uint  slices;
            uint  byBlock;
            uint  continuous;
            uint  responseCode;
            float   threshold;
            uint  triggerChannel;
            uint  triggerType;
            uint  triggerCode;
            float   refractory;
            uint  shiftLimit;
            uint  correlationThreshold;
            uint  enableShift;
            uint  insertEvents;
            uint  newRate;
            filter  filterSettings;	// filter settings;
        }*/
        /// <summary>
        ///     video structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Video
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            private readonly string csVideoID;
        }

        /// <summary>
        ///     Blink reduction parameters version 1
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlinkReduction
        {
            private readonly uint averages;
            private readonly float start;
            private readonly float stop;
            private readonly float threshold;
            private readonly uint direction;
            private readonly uint trigChan;
            private readonly uint artRej;
            private readonly float artMin;
            private readonly float artMax;
            private readonly uint displayOnly;
            private readonly uint externalTrigger;
            private readonly uint extTrigCode;
        }

        /*
        /// <summary>
        /// Blink reduction parameters version 2
        /// </summary>
        unsafe public struct blinkReduction2
        {
            uint  averages;
            float   start;
            float   stop;
            float   threshold;
            uint  direction;
            uint  trigChan;
            uint  artRej;
            float   artMin;
            float   artMax;
            uint  displayOnly;
            uint  externalTrigger;
            uint  extTrigCode;
            uint  bipolar;
            uint  bipolarRefChanIndex;
            uint  enableHighFilter;
            float   highFilter;
        }*/
        /// <summary>
        ///     Blink reduction parameters
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct AmpDescription
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            private readonly string ampName;
        }

        /// <summary>
        ///     High level inputs and outputs structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct HiLevelIo
        {
            private readonly float inputLow;
            private readonly float inputHigh;
            private readonly float outputLow;
            private readonly float outputHigh;
            private readonly uint enableExcitation;
            private readonly float excitationVoltage;
            private readonly uint disableFilter;
            private readonly uint customLabel;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            private readonly string customLabelStr;

            private readonly uint customUnits;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            private readonly string CustomUnits;

            private readonly float fDisplayScalar;
            private readonly uint bCustomScalar;
        }

        /// <summary>
        ///     re-referencing parameters
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ReferenceMode
        {
            private readonly uint refMode; //REFMODE_ACTIVE, REFMODE_VIRTUAL	

            private readonly uint virtualMode; //VIRTUALMODE_SINGLE, VIRTUALMODE_MULTI 

            //VIRTUALMODE_SINGLE (one reference channel digitally subtracted from each channel in DLL
            //VIRTUALMODE_MULTI (currently means all channels but could be some specified subset in the future)			
            private readonly uint virtualRefChanIndex; //channel to be digitally subtracted from others in DLL
            private readonly uint enableDeblock;
            private readonly uint deblockExtra;
            private readonly uint positiveEdge; // start de-blocking on positive/negative edge
        }

        /// <summary>
        ///     Basic parameters
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Basic
        {
            // base stuff
            private readonly uint type; // Acquisition type
            private readonly uint rate; // A-to-D rate
            private readonly uint channels; // Number of active channels
            private readonly uint notchFilterFrequency; // frequency of notch filter
            private readonly uint Domain; // Acquisition domain TIME = FALSE, FREQ=TRUE

            private readonly uint
                useCommonAmpSettings; // if <> 0, use common, otherwise individual electrodes (no Longer used!)

            [MarshalAs(UnmanagedType.Struct)] private readonly AmpInfo commonSettings; // Common amplifier settings
            [MarshalAs(UnmanagedType.Struct)] private readonly Filter commonFilter; // Common filter settings
            private readonly uint DCAutoCorrectFlag; // Auto-correct of DC values?
            private readonly uint DCThreshold; // DC level in percent for DC correction
            private readonly uint bACCoupling; // AC coupling on DC amp?
            private readonly uint bSingleWindow1Enable; // Start single window 1
            private readonly uint bSingleWindow2Enable; // Start single window 2
            private readonly uint bMultiWindowEnable; // Start multi-window 
            private readonly uint bSingleWindow1Filter; // Filter single window 1
            private readonly uint bSingleWindow2Filter; // Filter single window 2
            private readonly uint bMultiWindowFilter; // Filter multi-window 
            private readonly uint bSingleWindow1Derivation; // Derivation single window 1
            private readonly uint bSingleWindow2Derivation; // Derivation single window 2
            private readonly uint bMultiWindowDerivation; // Derivation multi-window 

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
            private readonly string szSingle1WindowLdr; // Single window 1 ldr file name

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
            private readonly string szSingle2WindowLdr; // Single window 2 ldr file name

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
            private readonly string szMultiWindowLdr; // Multi window ldr file name

            private readonly uint bAutoSave; // Start saving on startup
            private readonly uint bClearAverage; // Clear averages
            private readonly uint bMenuStartUp; // Menu on Startup
            private readonly uint bSound; // sound enable
            private readonly uint lVolume; // sound volume
            private readonly uint lBalance; // sound balance
            private readonly MapStruct Map1; // map struct 
            private readonly MapStruct Map2; // map struct 
            private readonly MapStruct Map3; // map struct 
            private readonly MapStruct Map4; // map struct 
            private readonly MapStruct Map5; // map struct 
            private readonly int nNumberOfAnnotationDefinitions; // number of annotation definitions
            private readonly int nNumberOfHotKeys; // number of hot key annotations
            private readonly uint bInvertWave; // invert waveform
            private readonly uint bVideoEnabled; // Enable Video capture
            private readonly int nMapVideo; //map number to be overladed on video

            private readonly int nAddAfter; // used for auto add to NSclipbrd

            // version 1 additions (Reserved[1018] for version 0)
            private readonly uint bVarianceDataAvailable;
            private readonly uint bDisplayVariance;
            private readonly uint varianceWaveColor; // sizeof(COLORREF) = 4 bytes
            private readonly uint dataType; // TYPE_AVGD_TIME or TYPE_EPOCH_TIME or TYPE_CONT_TIME etc;
            private readonly uint offLine; // TRUE for off-line (set by TransXxxFileIn classes)
            private readonly float frate; // floating point version of sampling rate (cf nRate)
            private readonly uint complex; // complex data, with both Real and Imaginary parts (versus Real only)
            private readonly uint cartesian; // Cartesian coordinates for complex data (versus Polar coordinates)

            private readonly uint realSymmetry; // complex numbers are stored without negative frequencies
            // end of version 1 additions (9*4 bytes)

            private readonly uint ampResolution; //resolution of amplifier (eg. 16 bits = 65536)
            private readonly float fRange; //dynamic range of a/d converter

            private readonly uint numberOfElectMarkers;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)]
            private readonly string refName; // Name of common reference

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)]
            private readonly string recordDate; // Date of registration

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 12)]
            private readonly string recordTime; // Time of registration

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            private readonly string comments; // Comments about the setup

            private readonly uint positionsPresent;
            private readonly uint majorVer;
            private readonly uint minorVer;
            private readonly float overallNoise;
            private readonly float bestSNR;
            private readonly uint scales;
            private readonly uint timeFrequency;
            private readonly uint customRate;
            private readonly uint enableCustomRate;
            private readonly uint inputCustomRate;
            private readonly uint showUncorrectedSWDISP2;
            private readonly uint saveCorrecteddData;
            private readonly ulong numCntPoints;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 968 * sizeof(uint))]
            private readonly string Reserved;
        }

        /// <summary>
        ///     Epoch parameters
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Epoch
        {
            private readonly uint Pnts; // Number of points per waveform
            private readonly uint PrestimPnts; // points of pre-stimulus, replaces xmin, xmax
            private readonly uint BaselineCorrection; // Baseline correct flag
            private readonly float BaselineCorrectionStart; // Start point for baseline correction
            private readonly float BaselineCorrectionStop; // Stop point for baseline correction
            private readonly uint Reject; // Auto reject flag
            private readonly float fRejStart; // Auto reject start point
            private readonly float fRejStop; // Auto reject stop point
            private readonly float fRejMin; // Auto reject minimum value
            private readonly float fRejMax; // Auto reject maximum value
            private readonly uint nSweeps; // Number of expected sweeps
            private readonly uint nAverageUpdate; // Number of sweeps per average update
            private readonly float fDisplayXMin; // Display X minimum
            private readonly float fDisplayXMax; // Display X maximum
            private readonly float fDisplayYMin; // Display Y minimum
            private readonly float fDisplayYMax; // Display Y maximum
            private readonly float fDisplayZMin; // Display Z minimum
            private readonly float fDisplayZMax; // Display Z maximum
            private readonly uint bDisplayYAutoScale; // Y Display autoscale
            private readonly uint bDisplayZAutoScale; // Z display autoscale
            private readonly uint bSortEnable; // Online sorting enable

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)]
            private readonly string sortFileName; // Sorting file name

            // version 1 additions (Reserved[200] for version 0)
            private readonly uint accept; //nAccept,nReject and nCompSweeps
            private readonly uint reject; //were added as a place to hold
            private readonly uint compSweeps; //these values from 30 files
            private readonly uint groups; //added to hold the (main) n value from the 30 file
            private readonly float mean_age; //added to hold the fmean value from the 3.0 format
            private readonly float meanLatency;
            private readonly float meanAccuracy;
            private readonly float xmin; // epoch minimum, s (latency of the first point)
            private readonly float xmax0; // epoch maximum, s,(latency of the last point)

            private readonly float xmax1; // latency of the point after the last, for display purposes

            // end of version 1 additions (10*4 bytes)
            private readonly uint saveSweepsInMemory; // Try and save all sweeps in memory

            // end of version 2 rrr
            [MarshalAs(UnmanagedType.I4, SizeConst = 189)]
            private readonly uint reserved; // must be set to 0
        }

        /// <summary>
        ///     Trigger parameters
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Trigger
        {
            private readonly uint triggerMode; // Trigger mode
            private readonly float triggerVoltageThreshold; // Trigger threshold for voltage triggering
            private readonly uint triggerVoltageSlope; // trigger slope, TRUE -> pos. FALSE -> neg.
            private readonly uint triggerVoltageChannel; // Trigger channel for voltage triggering
            private readonly uint triggerVoltageRectify; // Trigger rectify flag, TRUE -> rectify signal
            private readonly uint triggerExternalHold; // Hold value for external trigger
            private readonly uint triggerExternalInvert; // if TRUE, the amps invert the trigger hold value
            private readonly float triggerInternalInterval; // Inter-stimulus interval in seconds for internal trigger

            [MarshalAs(UnmanagedType.I4, SizeConst = 200)]
            private readonly uint teserve; // must be set to 0
        }

        /// <summary>
        ///     FSP averaging parameters
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Fsp
        {
            private readonly uint fspTerminateMethod; // FSP - Terminate Method:  0 -> none, 1 -> F value,

            //                           2 -> Noise level, 3 -> both
            private readonly uint fspTerminateChannels; // FSP - Terminate channels: 0 -> ALL, 1 -> SELECTED
            private readonly float fspFValue; // FSP - F value to stop terminate
            private readonly float fspSinglePointPos; // FSP - Single point location
            private readonly uint fspSweepsPerBlock; // FSP - block size for averaging
            private readonly float fspWindowStartPos; // FSP - Start of window
            private readonly float fspWindowStopPos; // FSP - Stop  of window
            private readonly float fspNoiseLevel; // FSP - Signal to ratio value
            private readonly float fspAlpha; // FSP - Alpha value
            private readonly uint fspV1; // FSP - degrees of freedom
            private readonly float fspDispFmax; // FSP - Display F maximum
            private readonly float fspDispNoiseMax; // FSP - Display noise maximum

            [MarshalAs(UnmanagedType.I4, SizeConst = 200)]
            private readonly uint Reserved; // must be set to 0
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FrequencyStruct
        {
            private readonly uint spectralEnable;
            private readonly uint spectralScalingMethod;
            private readonly uint spectralDisplayMethod;
            private readonly uint spectralAcquisitionMode;
            private readonly uint spectralMeanFrequency;
            private readonly uint spectralSaveData;
            private readonly uint spectralSweepsPerAverage;
            private readonly uint spectralWindowPoints;
            private readonly float spectralWindowLength;
            private readonly uint spectralWindowType;
            private readonly float spectralXmin;
            private readonly float spectralXmax;
            private readonly float spectralYmin;
            private readonly float spectralYmax;
            private readonly float spectralZmin;
            private readonly float spectralZmax;
            private readonly uint spectralAutoYScaling;

            private readonly uint spectralAutoZScaling;

            // version 1 additions
            private readonly float spectralBinSizeHz;
            private readonly float spectralFirstBinHz;

            private readonly float spectralLastBinHz;

            // version 2 additions
            private readonly uint spectralSweeps;

            private readonly uint spectralSmooth;

            // version 3 additions
            private readonly uint CWT;
            private readonly uint CWTChannel;
            private readonly float CWTMax;

            [MarshalAs(UnmanagedType.I4, SizeConst = 192)]
            private readonly uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Occular
        {
            private readonly uint veogCorrected; // has the data already been VEOG-corrected?
            private readonly float veogTrig; // trigger percentage (30: erp.veogtrig)
            private readonly uint veogChnl; // VEOG channel index
            private readonly uint veogDir; // trigger direction
            private readonly float veogDur; // time duration for average blink, sec
            private readonly uint veogSweeps; // number of sweeps included for average blink

            [MarshalAs(UnmanagedType.I4, SizeConst = 200)]
            private readonly uint reserved;
        }

        /// <summary>
        ///     Basic electrode attributes
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ElectrodeStructure
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            private readonly string label; // Electrode label - last byte contains '\0'

            private readonly uint reference; // Reference electrode number
            private readonly uint skip; // Skip electrode flag
            private readonly uint artifactRejection; // Artifact reject flag
            private readonly uint fspStop; // Fsp stop electrode
            private readonly uint bad; // Bad electrode flag
            private readonly uint hide; // Hide electrode?
            private readonly uint accept; // Number of Accepted sweeps
            private readonly uint reject; // Number of Rejected sweeps

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)]
            private readonly string xUnits; // specialized display units

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)]
            private readonly string yUnits; // specialized display units

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
            private readonly float[] scaleFactor; // Display scale factor	for each display page

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
            private readonly uint[] displayInPage; // electrode active on current display page

            // window coordinates (normalized) on parent screen
            // two sizes are used: "Small" and "Large",
            // small window
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20, ArraySubType = UnmanagedType.R4)]
            private readonly float[] fWinLeftS;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20, ArraySubType = UnmanagedType.R4)]
            private readonly float[] fWinRightS;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20, ArraySubType = UnmanagedType.R4)]
            private readonly float[] fWinTopS;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20, ArraySubType = UnmanagedType.R4)]
            private readonly float[] fWinBottomS;

            // large window
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20, ArraySubType = UnmanagedType.R4)]
            private readonly float[] fWinLeftL;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20, ArraySubType = UnmanagedType.R4)]
            private readonly float[] fWinRightL;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20, ArraySubType = UnmanagedType.R4)]
            private readonly float[] fWinTopL;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20, ArraySubType = UnmanagedType.R4)]
            private readonly float[] fWinBottomL;

            private readonly uint largeWindow; // TRUE -> set large window
            private readonly uint DCCoupled; // TRUE -> channel is DC coupled
            private readonly uint DisplayPage; // Display page
            private readonly float calibrationFactor; // Calibration factor
            private readonly uint PhysicalChannel; // Physical channel

            private readonly AmpInfo AmpSettings; // Channel information for amplifier

            private readonly Filter FilterSettings; // filter settings;
            private readonly Electrodes3D Position; // 3D position of electrode;

            private readonly SoundSetup Sound; // Sound settings

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            private readonly CompareSetup[] Compare; // Compare struct

            private readonly uint autoAdd; // Automatically add to NSClipboard after nAddAfter Averages
            private readonly uint autoAddLast; // Automatically add to NSClipboard at end of averaging	
            private readonly float SNR; // SNR value
            private readonly uint numSweeps; // Number of sweeps
            private readonly uint baselineCorrect; // Individual channel baseline correct
            private readonly uint multiWaveColor; // Multi window waveform Color
            private readonly uint singleWaveColor; // Single window waveform color
            private readonly uint enableCustomColorSingle; // Enable Custom Color in SWDisp
            private readonly uint enableCustomColorMulti; // Enable Custom Color in MWDisp
            private readonly uint impedance; // Impedance

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            private readonly uint[] compareLineStyle;

            private readonly AngleSetup angularPosition; // Position of electrode in angular coordinates on a sphere

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 161)]
            private readonly uint[] reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct MarkerProoperties
        {
            private readonly Electrodes3D ElectMarker;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            private readonly string label;

            [MarshalAs(UnmanagedType.U4, SizeConst = 20)]
            private readonly uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct FreqBand
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            private readonly string band;

            private readonly float fStart;
            private readonly float fStop;
            private readonly float fMin;
            private readonly float fMax;
            private readonly uint bRelative;

            [MarshalAs(UnmanagedType.U4, SizeConst = 20)]
            private readonly uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct SnrProperties
        {
            private readonly SnrMethod snrMethod;
            private readonly float fOverallSNR;
            private readonly float fBackgroundNoise;
            private readonly float fStart;
            private readonly uint fStop;

            [MarshalAs(UnmanagedType.U4, SizeConst = 20)]
            private readonly uint Reserved;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct NsElectrodeStruct
        {
            // Electrode structure  -------------------
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)]
            public string lab; // Electrode label - last bye contains NULL 

            [MarshalAs(UnmanagedType.U1)]
            public readonly byte refElectrode; // Reference electrode number               

            public readonly byte skip; // Skip electrode flag ON=1 OFF=0           
            public readonly byte reject; // Artifact reject flag                     
            public readonly byte display; // Display flag for 'STACK' display         
            public readonly byte bad; // Bad electrode flag                        
            public ushort n; // Number of observations                   
            public readonly byte avg_reference; // Average reference status                 
            public readonly byte ClipAdd; // Automatically add to clipboard           
            public readonly float x_coord; // X screen coordinates for 'TOP' display        
            public readonly float y_coord; // Y screen coordinates. for 'TOP' display        
            public readonly float veog_wt; // VEOG correction weight                   
            public readonly float veog_std; // VEOG std deviations. for weight                 
            public readonly float snr; // signal-to-noise statistic                
            public readonly float heog_wt; // HEOG Correction weight                   
            public readonly float heog_std; // HEOG Std deviations. for weight                 
            public readonly ushort baseline; // Baseline correction value in raw ad units
            public readonly byte Filtered; // Toggle indicating file has be filtered   
            public readonly byte Fsp; // Extra data                               
            public readonly float aux1_wt; // AUX1 Correction weight                    
            public readonly float aux1_std; // AUX1 Std deviations for weight                 
            public readonly float sensitivity; // electrode sensitivity                    
            public readonly byte Gain; // Amplifier gain                           
            public readonly byte HiPass; // Hi Pass value                            
            public readonly byte LoPass; // Lo Pass value                            
            public readonly byte Page; // Display page                             
            public readonly byte Size; // Electrode window display size            
            public readonly byte Impedance; // Impedance test                           
            public readonly byte PhysicalChnl; // Physical channel used                          
            public readonly byte Rectify; // Free space                                     
            public float calib; // Calibration factor                       
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct NsHeaderStruct
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 12)]
            public string rev; // Revision string

            [MarshalAs(UnmanagedType.U4, SizeConst = 4)]
            public uint NextFile; // offset to next file

            [MarshalAs(UnmanagedType.U4, SizeConst = 4)]
            public uint PrevFile; // offset to prev file 

            public byte type; // File type AVG=0, EEG=1, etc.

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            public string id; // Patient ID 

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            public string oper; // Operator ID  

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            public string doctor; // Doctor ID  

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            public readonly string referral; // Referral ID     

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            public string hospital; // Hospital ID  

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            public string patient; // Patient name                            

            public ushort age; // Patient Age                             
            public char sex; // Patient Sex Male='M', Female='F'        
            public char hand; // Handedness Mixed='M',Rt='R', lft='L' 

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            public string med; // Medications  

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            public string category; // Classification   

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            public string state; // Patient wakefulness 

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
            public string label; // Session label   

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)]
            public string date; // Session date string  

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 12)]
            public readonly string time; // Session time string  

            public float mean_age; // Mean age (Group files only)             
            public readonly float stdev; // Std dev of age (Group files only)       
            public ushort n; // Number in group file 

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 38)]
            public readonly string compfile; // Path and name of comparison file        

            public readonly float SpectWinComp; // Spectral window compensation factor
            public float MeanAccuracy; // Average response accuracy
            public float MeanLatency; // Average response latency

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 46)]
            public readonly string sortfile; // Path and name of sort file              

            public int NumEvents; // Number of events in event table
            public readonly byte compoper; // Operation used in comparison            
            public byte avgmode; // Set during on-line averaging             
            public readonly byte review; // Set during review of EEG data           
            public ushort nsweeps; // Number of expected sweeps          
            public ushort compsweeps; // Number of actual sweeps            
            public ushort acceptcnt; // Number of accepted sweeps          
            public ushort rejectcnt; // Number of rejected sweeps          
            public ushort pnts; // Number of points per waveform      
            public ushort nchannels; // Number of active chaFbnnels        
            public ushort avgupdate; // Frequency of average update        
            public byte domain; // Acquisition domain TIME=0, FREQ=1       
            public byte variance; // Variance data included flag             
            public ushort rate; // A-to-D rate                             
            public double scale; // scale factor for calibration            
            public byte veogcorrect; // VEOG corrected flag                     
            public byte heogcorrect; // HEOG corrected flag                     
            public byte aux1correct; // AUX1 corrected flag                     
            public byte aux2correct; // AUX2 corrected flag                     
            public readonly float veogtrig; // VEOG trigger percentage                 
            public readonly float heogtrig; // HEOG trigger percentage                 
            public readonly float aux1trig; // AUX1 trigger percentage                 
            public readonly float aux2trig; // AUX2 trigger percentage                 
            public readonly ushort heogchnl; // HEOG channel number                     
            public readonly ushort veogchnl; // VEOG channel number                     
            public readonly ushort aux1chnl; // AUX1 channel number                     
            public readonly ushort aux2chnl; // AUX2 channel number                     
            public readonly byte veogdir; // VEOG trigger direction flag             
            public readonly byte heogdir; // HEOG trigger direction flag             
            public readonly byte aux1dir; // AUX1 trigger direction flag             
            public readonly byte aux2dir; // AUX2 trigger direction flag             
            public readonly ushort veog_n; // Number of points per VEOG waveform      
            public readonly ushort heog_n; // Number of points per HEOG waveform      
            public readonly ushort aux1_n; // Number of points per AUX1 waveform      
            public readonly ushort aux2_n; // Number of points per AUX2 waveform      
            public readonly ushort veogmaxcnt; // Number of observations per point - VEOG 
            public readonly ushort heogmaxcnt; // Number of observations per point - HEOG 
            public readonly ushort aux1maxcnt; // Number of observations per point - AUX1 
            public readonly ushort aux2maxcnt; // Number of observations per point - AUX2 
            public readonly byte veogmethod; // Method used to correct VEOG             
            public readonly byte heogmethod; // Method used to correct HEOG             
            public readonly byte aux1method; // Method used to correct AUX1             
            public readonly byte aux2method; // Method used to correct AUX2             
            public readonly float AmpSensitivity; // External Amplifier gain                 
            public readonly byte LowPass; // Toggle for Amp Low pass filter          
            public readonly byte HighPass; // Toggle for Amp High pass filter         
            public readonly byte Notch; // Toggle for Amp Notch state              
            public readonly byte AutoClipAdd; // AutoAdd on clip                         
            public readonly byte baseline; // Baseline correct flag                   
            public readonly float offstart; // Start point for baseline correction     
            public readonly float offstop; // Stop point for baseline correction      
            public readonly byte reject; // Auto reject flag                        
            public readonly float rejstart; // Auto reject start point                 
            public readonly float rejstop; // Auto reject stop point                  
            public readonly float rejmin; // Auto reject minimum value               
            public readonly float rejmax; // Auto reject maximum value               
            public readonly byte trigtype; // Trigger type                            
            public readonly float trigval; // Trigger value                           
            public readonly byte trigchnl; // Trigger channel                         
            public readonly ushort trigmask; // Wait value for LPT port                 
            public readonly float trigisi; // Inter-stimulus interval (INT trigger)    
            public readonly float trigmin; // Min trigger out voltage (start of pulse)
            public readonly float trigmax; // Max trigger out voltage (during pulse)  
            public readonly byte trigdir; // Duration of trigger out pulse           
            public readonly byte Autoscale; // Autoscale on average                    
            public readonly ushort n2; // Number in group 2 (MANOVA)              
            public readonly byte dir; // Negative display up or down             
            public readonly float dispmin; // Display minimum (Y-axis)                 
            public readonly float dispmax; // Display maximum (Y-axis)                 
            public float xmin; // X axis minimum (epoch start in sec)     
            public float xmax; // X axis maximum (epoch stop in sec)      
            public readonly float AutoMin; // Autoscale minimum                       
            public readonly float AutoMax; // Autoscale maximum                       
            public readonly float zmin; // Z axis minimum - Not currently used     
            public readonly float zmax; // Z axis maximum - Not currently used     
            public readonly float lowcut; // Archival value - low cut on external amp
            public readonly float highcut; // Archival value - Hi cut on external amp 
            public readonly byte common; // Common mode rejection flag              
            public readonly byte savemode; // Save mode EEG AVG or BOTH               
            public readonly byte manmode; // Manual rejection of incoming data

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 10)]
            public string refname; // Label for reference electrode           

            public readonly byte Rectify; // Rectification on external channel       
            public float DisplayXmin; // Minimum for X-axis display              
            public float DisplayXmax; // Maximum for X-axis display              
            public readonly byte phase; // flag for phase computation

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
            public string screen; // Screen overlay path name               

            public readonly ushort CalMode; // Calibration mode                        
            public readonly ushort CalMethod; // Calibration method                      
            public readonly ushort CalUpdate; // Calibration update rate                 
            public readonly ushort CalBaseline; // Baseline correction during cal          
            public readonly ushort CalSweeps; // Number of calibration sweeps            
            public readonly float CalAttenuator; // Attenuator value for calibration        
            public readonly float CalPulseVolt; // Voltage for calibration pulse           
            public readonly float CalPulseStart; // Start time for pulse                    
            public readonly float CalPulseStop; // Stop time for pulse                     
            public readonly float CalFreq; // Sweep frequency  

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 34)]
            public string taskfile; // Task file name

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 34)]
            public string seqfile; // Sequence file path name                

            public readonly byte SpectMethod; // Spectral method
            public readonly byte SpectScaling; // Scaling employed
            public readonly byte SpectWindow; // Window employed
            public readonly float SpectWinLength; // Length of window 
            public readonly byte SpectOrder; // Order of Filter for Max Entropy method
            public readonly byte NotchFilter; // Notch Filter in or out
            public readonly short HeadGain; // Current head gain for SYNAMP
            public readonly uint AdditionalFiles; // No of additional files

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 5)]
            public readonly string unused; // Free space

            public readonly short FspStopMethod; // FSP - stopping mode                      
            public readonly short FspStopMode; // FSP - stopping mode                      
            public readonly float FspFValue; // FSP - F value to stop terminate         
            public readonly ushort FspPoint; // FSP - Single point location             
            public readonly ushort FspBlockSize; // FSP - block size for averaging          
            public readonly ushort FspP1; // FSP - Start of window                   
            public readonly ushort FspP2; // FSP - Stop  of window                   
            public readonly float FspAlpha; // FSP - Alpha value                       
            public readonly float FspNoise; // FSP - Signal to ratio value             
            public readonly ushort FspV1; // FSP - degrees of freedom

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)]
            public readonly string montage; // Montage file path name

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)]
            public string EventFile; // Event file path name                  

            public readonly float fratio; // Correction factor for spectral array    
            public readonly byte minor_rev; // Current minor revision                  
            public readonly ushort eegupdate; // How often incoming EEG is refreshed    
            public readonly byte compressed; // Data compression flag                   
            public readonly float xscale; // X position for scale box - Not used     
            public readonly float yscale; // Y position for scale box - Not used     
            public readonly float xsize; // Waveform size X direction               
            public readonly float ysize; // Waveform size Y direction               
            public readonly byte ACmode; // Set SYNAMP into AC mode                  
            public readonly byte CommonChnl; // Channel for common waveform    
            public readonly byte Xtics; // Scale tool- 'tic' flag in X direction   
            public readonly byte Xrange; // Scale tool- range (ms,sec,Hz) flag X dir
            public readonly byte Ytics; // Scale tool- 'tic' flag in Y direction   
            public readonly byte Yrange; // Scale tool- range (uV, V) flag Y dir    
            public readonly float XScaleValue; // Scale tool- value for X dir             
            public readonly float XScaleInterval; // Scale tool- interval between tics X dir 
            public readonly float YScaleValue; // Scale tool- value for Y dir             
            public readonly float YScaleInterval; // Scale tool- interval between tics Y dir 
            public readonly float ScaleToolX1; // Scale tool- upper left hand screen pos  
            public readonly float ScaleToolY1; // Scale tool- upper left hand screen pos  
            public readonly float ScaleToolX2; // Scale tool- lower right hand screen pos 
            public readonly float ScaleToolY2; // Scale tool- lower right hand screen pos 
            public ushort port; // Port address for external triggering 

            [MarshalAs(UnmanagedType.U4, SizeConst = 4)]
            public uint NumSamples; // Number of samples in continuous file     

            public readonly byte FilterFlag; // Indicates that file has been filtered   
            public readonly float LowCutoff; // Low frequency cutoff                    
            public readonly ushort LowPoles; // Number of poles                         
            public readonly float HighCutoff; // High frequency cutoff                   
            public readonly ushort HighPoles; // High cutoff number of poles             
            public readonly byte FilterType; // Bandpass=0 Notch=1 High pass=2 Low pass=3 
            public readonly byte FilterDomain; // Frequency=0 Time=1                      
            public readonly byte SnrFlag; // SNR computation flag                    
            public readonly byte CoherenceFlag; // Coherence has been  computed            
            public byte continuousType; // Method used to capture events in *.cnt 

            [MarshalAs(UnmanagedType.U4, SizeConst = 4)]
            public uint EventTablePos; // Position of event table                 

            public float continuousSeconds; // Number of seconds to displayed per page

            [MarshalAs(UnmanagedType.U4, SizeConst = 4)]
            public readonly uint ChannelOffset; // Block size of one channel in SYNAMPS

            public byte AutoCorrectFlag; // Auto correct of DC values
            public readonly byte DCThreshold; // Auto correct of DC level 
        }

        // DEFINITIONS OF EVENT TYPES 
        //
        // This structure describes an event type 0 in a continuous file
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Event1
        {
            private readonly ushort stimType; //range  0-65535
            private readonly byte keyBoard; //range  0-11  corresponding to function keys +1
            private readonly byte keyPad; //range  0-15  bit coded response pad and keypad
            private readonly uint offset; //file offset of event  
        }

        /// <summary>
        ///     This structure describes an event type 1 in a continuous file
        ///     It  contains addition information regarding subject performance in
        ///     behavioral task
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Event2
        {
            private readonly Event1 Event1;
            private readonly ushort eventType;
            private readonly ushort eventCode;
            private readonly float responseLatency;
            private readonly byte epochEvent;
            private readonly byte accept;
            private readonly byte accuracy;
        }

        // This structure describes a tag type 0 in a continuous file
        /// <summary>
        ///     This structure describes a tag type 0 in a continuous file
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Teeg
        {
            private readonly byte teeg;

            public readonly long Size;
            //Int16 ptr;
            //long Offset;    //Relative file position 
            //0 Means the data start immediately
            //>0 Means the data starts at a relative offset
            // from current position at the end of the tag
        }

        /// <summary>
        ///     subject
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct SubjectInfo
        {
            private readonly int age;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string date;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string department;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string strDOB;

            public readonly float fHeight;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string ID;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string Institute;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public readonly string Medications;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string Name;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string Operator;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string Referral;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            private readonly string Researcher;

            public readonly float Temperature;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string Time;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string Unit;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public readonly string Wakefulness;

            private readonly float Weight;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string Comments;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string Ethnicity;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 21)]
            public readonly string Language;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public readonly string Session;

            public readonly GenderType GenderId;
            public readonly HandPreference Handedness;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct TagId
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 7)]
            private readonly string nsi_tff;
        }
    }
}