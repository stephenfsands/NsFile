using System.Windows;
using System.IO;
using NSFileAccess;
using EegLabFile;


namespace NSFileTest
{

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow() => InitializeComponent();

        private static EegLab _eegLab = new EegLab();
        private static NsFile _dataFile = new NsFile();
        private void Read_File_Click(object sender, RoutedEventArgs e)
        {

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                FileName = "Data File",
                DefaultExt = ".cnt",
                Filter = "Neuroscan files|*.cnt;*.avg;*.eeg|EEG Lab|*.data|All files|*.*"
            };

            // Show open file dialog box
            var result = dlg.ShowDialog();

            // Process open file dialog box results
            if (result != true) return;
            var extension = Path.GetExtension(dlg.FileName);

            // Matlab processing
            if (extension == ".data")
            {
                _eegLab.ReadHeaderFile(dlg.FileName);
                _eegLab.FileType = NsFile.FileTypes.Continuous;
                _eegLab.ReadDataFile(dlg.FileName);


            }
            //Neuroscan processing
            else
            {
                // Open document
                var filename = dlg.FileName;
                _dataFile.LoadFile(filename);
                HeaderListBox.Items.Add("header in");
                if (_dataFile.FileType == NsFile.FileTypes.Continuous)
                {
                    _dataFile.ReadEvents();
                    HeaderListBox.Items.Add("events in");
                    _dataFile.ReadCntData();
                    HeaderListBox.Items.Add("Continuous data read");
                }
                if (_dataFile.FileType == NsFile.FileTypes.Average)
                {
                    _dataFile.ReadAvgData();
                }
                HeaderListBox.Items.Add("data in");
                _dataFile.ReadClose();
                HeaderListBox.Items.Add("reader closed");
            }
        }
        private void Write_File_Click(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Neuroscan files|*.cnt;*.avg;*.eeg|All files|*.*",
                FilterIndex = 2,
                RestoreDirectory = true
            };


            var result = dlg.ShowDialog();

            if (result != true) return;
            // Process open file dialog box results
            {
                // Open document
                var filename = dlg.FileName;
                var name = Path.GetFileNameWithoutExtension(filename);
                var ret=false;
                if (_dataFile.FileType == NsFile.FileTypes.Continuous)
                {
                    name += ".cnt";
                    ret=_dataFile.WriteData(name);
                    if (ret) HeaderListBox.Items.Add("Cnt file out");
                }
                else
                {
                    name += ".avg";
                    ret=_dataFile.WriteData(name);
                    if (ret) HeaderListBox.Items.Add("Cnt file out");
                }
            }
        }
    }
}
