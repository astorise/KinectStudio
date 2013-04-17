﻿using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.IO;
using System.Windows;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Threading;
using Microsoft.Kinect;
using Microsoft.Win32;
using System.Diagnostics;
//using Emgu.CV;
//using Emgu.CV.Structure;
//using Emgu.Util;
using KinectMotionAnalyzer.Model;


namespace KinectMotionAnalyzer.UI
{
    using KinectMotionAnalyzer.Processors;


    /// <summary>
    /// Interaction logic for GestureRecognizerWindow.xaml
    /// </summary>
    public partial class MotionAnalyzerWindow : Window
    {
        // tools
        private KinectDataManager kinect_data_manager;
        private KinectSensor kinect_sensor;
        private MotionAssessor motion_assessor = null;

        // recognition
        private GestureRecognizer gesture_recognizer = null;
        private string GESTURE_DATABASE_DIR = "gdata\\";

        // sign
        bool isReplay = false;
        bool isRecognition = false;
        bool isStreaming = false;
        bool ifDoSmoothing = true;
        bool isCalculating = false; // a lock param for multithreading

        // record params
        private int frame_id = 0;
        Gesture temp_gesture = new Gesture();
        ArrayList overlap_frame_rec_buffer; // use to store record frames in memory
        List<Skeleton> skeleton_rec_buffer; // record skeleton data
        List<byte[]> color_frame_rec_buffer; // record video frames

        // motion analysis params
        private List<MeasurementUnit> toMeasureUnits;


        public MotionAnalyzerWindow()
        {
            InitializeComponent();
        }


        /// <summary>
        /// initialize kinect sensor and init data members
        /// </summary>
        private bool InitKinect()
        {
            // enumerate and fetch an available sensor
            foreach (var potentialsensor in KinectSensor.KinectSensors)
            {
                if (potentialsensor.Status == KinectStatus.Connected)
                {
                    kinect_sensor = potentialsensor;
                    break;
                }
            }

            // enable data stream
            if (kinect_sensor != null)
            {
                // initialize data manager
                kinect_data_manager = new KinectDataManager(ref kinect_sensor);
                //replay_data_manager = new KinectDataManager(ref kinect_sensor);

                // initialize stream
                kinect_sensor.ColorStream.Enable(ColorImageFormat.RgbResolution640x480Fps30);
                if (ifDoSmoothing)
                {
                    TransformSmoothParameters smoothingParam = new TransformSmoothParameters();
                    {
                        // Some smoothing with little latency (defaults).
                        // Only filters out small jitters.
                        // Good for gesture recognition in games.
                        smoothingParam.Smoothing = 0.5f;
                        smoothingParam.Correction = 0.5f;
                        smoothingParam.Prediction = 0.5f;
                        smoothingParam.JitterRadius = 0.05f;
                        smoothingParam.MaxDeviationRadius = 0.04f;

                        // Smoothed with some latency.
                        // Filters out medium jitters.
                        // Good for a menu system that needs to be smooth but
                        // doesn't need the reduced latency as much as gesture recognition does.
                        //smoothingParam.Smoothing = 0.5f;
                        //smoothingParam.Correction = 0.1f;
                        //smoothingParam.Prediction = 0.5f;
                        //smoothingParam.JitterRadius = 0.1f;
                        //smoothingParam.MaxDeviationRadius = 0.1f;

                        //// Very smooth, but with a lot of latency.
                        //// Filters out large jitters.
                        //// Good for situations where smooth data is absolutely required
                        //// and latency is not an issue.
                        //smoothingParam.Smoothing = 0.7f;
                        //smoothingParam.Correction = 0.3f;
                        //smoothingParam.Prediction = 1.0f;
                        //smoothingParam.JitterRadius = 1.0f;
                        //smoothingParam.MaxDeviationRadius = 1.0f;
                    };

                    kinect_sensor.SkeletonStream.Enable(smoothingParam);
                }
                else
                    kinect_sensor.SkeletonStream.Enable();


                // set source (must after source has been initialized otherwise it's null forever)
                kinect_data_manager.ColorStreamBitmap = new WriteableBitmap(
                    kinect_sensor.ColorStream.FrameWidth, kinect_sensor.ColorStream.FrameHeight, 96, 96,
                    PixelFormats.Bgr32, null);
                color_disp_img.Source = kinect_data_manager.ColorStreamBitmap;
                ske_disp_img.Source = kinect_data_manager.skeletonImageSource;

                // bind event handlers
                kinect_sensor.ColorFrameReady += kinect_colorframe_ready;
                kinect_sensor.SkeletonFrameReady += kinect_skeletonframe_ready;
            }
            else
            {
                // invalidate all buttons
                gestureReplayBtn.IsEnabled = false;

                return false;
            }

            return true;
        }


        void kinect_colorframe_ready(object sender, ColorImageFrameReadyEventArgs e)
        {

            using (ColorImageFrame frame = e.OpenColorImageFrame())
            {
                if (frame == null)
                    return;

                if (gestureCaptureBtn.Content.ToString() == "Stop Capture")
                {
                    // consistent with skeleton data
                    if (skeleton_rec_buffer.Count > 0)
                    {
                        byte[] colorData = new byte[frame.PixelDataLength];
                        frame.CopyPixelDataTo(colorData);
                        color_frame_rec_buffer.Add(colorData);
                    }
                }

                kinect_data_manager.UpdateColorData(frame); 
            }
        }

        void kinect_skeletonframe_ready(object sender, SkeletonFrameReadyEventArgs e)
        {

            using (SkeletonFrame frame = e.OpenSkeletonFrame())
            {
                if (frame == null)
                    return;

                // get skeleton data
                Skeleton[] skeletons = new Skeleton[frame.SkeletonArrayLength];
                frame.CopySkeletonDataTo(skeletons);

                // get first tracked skeleton
                Skeleton tracked_skeleton = null;
                foreach (Skeleton ske in skeletons)
                {
                    if (ske.TrackingState == SkeletonTrackingState.Tracked)
                    {
                        tracked_skeleton = ske;
                        break;
                    }
                }

                if (tracked_skeleton == null)
                    return;

                // if capturing, add to gesture data
                if (gestureCaptureBtn.Content.ToString() == "Stop Capture")
                {
                    // just add first tracked skeleton, assume only one person is present
                    skeleton_rec_buffer.Add(tracked_skeleton);
                }

                if (kinect_data_manager.ifShowJointStatus)
                {
                    //if (!isCalculating)
                    //{
                    //    // start new thread to do processing
                    //    Thread thread = new Thread(motion_assessor.UpdateJointStatus);
                    //    thread.Start(
                    //}
                    // update status
                    motion_assessor.UpdateJointStatus(tracked_skeleton, toMeasureUnits);
                    kinect_data_manager.cur_joint_status = motion_assessor.GetCurrentJointStatus();
                    kinect_data_manager.toMeasureUnits = this.toMeasureUnits;

                    // show feedback
                    //feedback_textblock.Text = motion_assessor.GetFeedbackForCurrentStatus();
                }

                kinect_data_manager.UpdateSkeletonData(tracked_skeleton);

                if (saveVideoCheckBox.IsChecked.Value)
                {
                    // save skeleton data
                    skeleton_rec_buffer.Add(tracked_skeleton);

                    // write screen shot of display into video file
                    int width = (int)groupBox3.Width + 20;
                    int height = (int)groupBox3.Height + 20;
                    System.Drawing.Rectangle bounds = new System.Drawing.Rectangle(
                        (int)(Application.Current.MainWindow.Left + groupBox3.Margin.Left),
                        (int)(Application.Current.MainWindow.Top + groupBox3.Margin.Top),
                        width, height);
                    Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);

                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(new System.Drawing.Point(bounds.Left, bounds.Top),
                            new System.Drawing.Point(-1, -1), bounds.Size);
                    }

                    overlap_frame_rec_buffer.Add(bitmap);
                }
            }
        }

        #region gesture_management

        private void gestureCaptureBtn_Click(object sender, RoutedEventArgs e)
        {
            if (gestureCaptureBtn.Content.ToString() == "Capture")
            {
                // check if type is selected
                if (gestureComboBox.SelectedIndex <= 0)
                {
                    MessageBox.Show("Select an action type before capturing.");
                    return;
                }

                // reset
                frame_id = 0;
                color_frame_rec_buffer.Clear();
                skeleton_rec_buffer.Clear();
                gestureCaptureBtn.Content = "Stop Capture";
                previewBtn.IsEnabled = false;
                
                // start kinect
                if (kinect_sensor == null)
                    return;

                if (!kinect_sensor.IsRunning)
                {
                    // can't replay since share same gesture buffer
                    DeactivateReplay();
                    gestureReplayBtn.IsEnabled = false;

                    kinect_sensor.Start();
                }
            }
            else
            {
                kinect_sensor.Stop();

                // prepare for replay
                if (skeleton_rec_buffer != null)
                {
                    ActivateReplay(color_frame_rec_buffer, skeleton_rec_buffer);
                    saveGestureBtn.IsEnabled = true;
                }

                gestureCaptureBtn.Content = "Capture";
                gestureReplayBtn.IsEnabled = true;
                previewBtn.IsEnabled = true;
                isRecognition = false;
            }
        }

        private void saveGestureBtn_Click(object sender, RoutedEventArgs e)
        {
            //// save to file
            //string time = System.DateTime.Now.ToString("hh'-'mm'-'ss", CultureInfo.CurrentUICulture.DateTimeFormat);

            //string gesture_name = (gestureComboBox.SelectedItem as ComboBoxItem).Content.ToString();
            //string savedir = "gdata\\"; //Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            //if(!Directory.Exists(savedir))
            //    Directory.CreateDirectory(savedir);

            //string skeletonpath = savedir + gesture_name + "\\Kinect_skeleton_" + time + ".xml";

            // save to database

            // save data from start label to end label
            int start_id = int.Parse(replay_startLabel.Content.ToString());
            int end_id = int.Parse(replay_endLabel.Content.ToString());
            // remove end part first so front id will not change
            if (color_frame_rec_buffer.Count > 0 && skeleton_rec_buffer.Count > 0)
            {
                // clean data
                color_frame_rec_buffer.RemoveRange(end_id + 1, color_frame_rec_buffer.Count - end_id - 1);
                color_frame_rec_buffer.RemoveRange(0, start_id);

                skeleton_rec_buffer.RemoveRange(end_id + 1, skeleton_rec_buffer.Count - end_id - 1);
                skeleton_rec_buffer.RemoveRange(0, start_id);

                // convert to kinect action for saving
                KinectAction rec_action = new KinectAction();
                rec_action.ActionName = (gestureComboBox.SelectedItem as ComboBoxItem).Content.ToString();
                rec_action.ColorFrames = new List<ColorFrameData>();
                //rec_action.Skeletons = new List<SkeletonData>();

                for (int i = 0; i < color_frame_rec_buffer.Count; i++)
                {
                    ColorFrameData colorFrame = new ColorFrameData();
                    colorFrame.FrameData = color_frame_rec_buffer[i];
                    colorFrame.FrameWidth = kinect_sensor.ColorStream.FrameWidth;
                    colorFrame.FrameHeight = kinect_sensor.ColorStream.FrameHeight;
                    colorFrame.FrameId = i;

                    rec_action.ColorFrames.Add(colorFrame);
                }
                //for (int i = 0; i < skeleton_rec_buffer.Count; i++)
                //{
                //    SkeletonData skeData = new SkeletonData();
                //    skeData.Status = (int)skeleton_rec_buffer[i].TrackingState;
                //    skeData.JointsData = new List<SingleJoint>();
                //    foreach (JointType jtype in Enum.GetValues(typeof(JointType)))
                //    {
                //        SingleJoint cur_joint = new SingleJoint();
                //        cur_joint.PosX = skeleton_rec_buffer[i].Joints[jtype].Position.X;
                //        cur_joint.PosY = skeleton_rec_buffer[i].Joints[jtype].Position.Y;
                //        cur_joint.PosZ = skeleton_rec_buffer[i].Joints[jtype].Position.Z;
                //        cur_joint.Type = (int)jtype;
                //        skeData.JointsData.Add(cur_joint);
                //    }

                //    rec_action.Skeletons.Add(skeData);
                //}

                KinectRecorder.WriteActionToDatabase(rec_action);
            }
            
            //KinectRecorder.WriteToSkeletonXMLFile(skeletonpath, skeleton_rec_buffer);
            //skeleton_rec_buffer.Clear();
            //frame_id = 0; 
            //statusbarLabel.Content = "Save skeletons to file: " + skeletonpath;

            color_frame_rec_buffer.Clear();
            skeleton_rec_buffer.Clear();

            statusbarLabel.Content = "Save action to database.";

            DeactivateReplay();
            saveGestureBtn.IsEnabled = false;
        }

        private void add_gesture_btn_Click(object sender, RoutedEventArgs e)
        {
            // open add window
            GestureConfigWin add_win = new GestureConfigWin();
            if (add_win.ShowDialog().Value == true)
            {
                gesture_recognizer.AddGestureConfig(add_win.new_gesture_config);

                // update list
                UpdateGestureComboBox();
                // update status bar
                statusbarLabel.Content = "Add gesture: " + add_win.new_gesture_config.name;
            }
        }

        private void remove_gesture_btn_Click(object sender, RoutedEventArgs e)
        {
            // remove gesture config of current selected one
            int gid = gestureComboBox.SelectedIndex;
            if (gid == 0)    // can't delete default one
            {
                MessageBox.Show("Select a valid gesture to remove.");
                return;
            }

            ComboBoxItem toRemoveItem = gestureComboBox.Items[gid] as ComboBoxItem;
            gesture_recognizer.RemoveGestureConfig(toRemoveItem.Content.ToString());

            // update ui
            UpdateGestureComboBox();
            // update status bar
            statusbarLabel.Content = "Remove gesture: " + toRemoveItem.Content;
        }

        #endregion

        private void UpdateGestureComboBox()
        {

            gestureComboBox.Items.Clear();
            // add prompt item
            ComboBoxItem prompt = new ComboBoxItem();
            prompt.Content = "Choose Gesture";
            prompt.IsEnabled = false;
            prompt.IsSelected = true;
            gestureComboBox.Items.Add(prompt);

            // add item for each gesture type
            foreach (string gname in gesture_recognizer.GESTURE_LIST.Values)
            {
                ComboBoxItem item = new ComboBoxItem();
                item.Content = gname;
                gestureComboBox.Items.Add(item);
            }

        }

        #region gesture_replay

        private void skeletonVideoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // valid only when kinect is stopped so no new data will come
            if (isReplay && skeleton_rec_buffer.Count > 0)
            {
                // load new skeleton data
                int cur_frame_id = (int)skeletonVideoSlider.Value;
                if (skeleton_rec_buffer.Count > cur_frame_id && color_frame_rec_buffer.Count > cur_frame_id)
                {
                    kinect_data_manager.UpdateColorData(color_frame_rec_buffer[cur_frame_id], 640, 480);
                    kinect_data_manager.UpdateSkeletonData(skeleton_rec_buffer[cur_frame_id]);
                }

                // update label
                skeletonSliderLabel.Content = skeletonVideoSlider.Value.ToString();
            }
        }

        private void gestureReplayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (kinect_sensor != null && kinect_sensor.IsRunning)
                return;

            OpenFileDialog dialog = new OpenFileDialog();
            dialog.DefaultExt = ".xml";
            dialog.FileName = "Skeleton";
            dialog.Filter = "Skeleton data file (.xml)|*.xml";

            Nullable<bool> result = dialog.ShowDialog();

            if (result == true)
            {
                string filename = dialog.FileName;
                // test: read skeleton data and display
                KinectRecorder.ReadFromSkeletonXMLFile(filename, out skeleton_rec_buffer);

                statusbarLabel.Content = "Load gesture file from " + filename;

                ActivateReplay(color_frame_rec_buffer, skeleton_rec_buffer);

                isReplay = true;
            }
        }

        private void ActivateReplay(List<byte[]> color_frame_rec_buffer, List<Skeleton> skeleton_rec_buffer)
        {
            if (color_frame_rec_buffer == null || color_frame_rec_buffer.Count == 0 || 
                skeleton_rec_buffer == null || skeleton_rec_buffer.Count == 0)
            {
                statusbarLabel.Content = "Replay gesture is empty.";
                return;
            }

            //gesture_data = gesture;

            int min_frame_id = 0;
            int max_frame_id = Math.Min(color_frame_rec_buffer.Count, skeleton_rec_buffer.Count) - 1;

            skeletonVideoSlider.IsEnabled = true;
            skeletonVideoSlider.Minimum = min_frame_id;
            skeletonVideoSlider.Maximum = max_frame_id;
            skeletonVideoSlider.Value = min_frame_id;
            skeletonSliderLabel.Content = min_frame_id.ToString();

            replay_setStartBtn.IsEnabled = true;
            replay_startLabel.Content = min_frame_id.ToString();
            replay_setEndBtn.IsEnabled = true;
            replay_endLabel.Content = max_frame_id;

            keyframeConfigBtn.IsEnabled = true;

            isReplay = true;

            // update view
            kinect_data_manager.UpdateColorData(color_frame_rec_buffer[min_frame_id], 640, 480);
            kinect_data_manager.UpdateSkeletonData(skeleton_rec_buffer[min_frame_id]);
        }

        private void DeactivateReplay()
        {
            // clear all
            skeletonVideoSlider.IsEnabled = false;
            skeletonVideoSlider.Minimum = 0;
            skeletonVideoSlider.Maximum = 0;
            skeletonVideoSlider.Value = 0;
            skeletonSliderLabel.Content = "0";

            replay_setStartBtn.IsEnabled = false;
            replay_startLabel.Content = "0";
            replay_setEndBtn.IsEnabled = false;
            replay_endLabel.Content = "0";

            keyframeConfigBtn.IsEnabled = false;

            isReplay = false;
        }

        private void replay_setStartBtn_Click(object sender, RoutedEventArgs e)
        {
            replay_startLabel.Content = skeletonVideoSlider.Value.ToString();
        }

        private void replay_setEndBtn_Click(object sender, RoutedEventArgs e)
        {
            if (skeletonVideoSlider.Value < double.Parse(replay_startLabel.Content.ToString()))
            {
                MessageBox.Show("End frame can't be earlier than start frame.");
                return;
            }

            replay_endLabel.Content = skeletonVideoSlider.Value;
        }

        #endregion

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (kinect_sensor != null)
                kinect_sensor.Stop();
        }

        private void previewBtn_Click(object sender, RoutedEventArgs e)
        {
            if (previewBtn.Content.ToString() == "Start Test")
            {
                if (kinect_sensor != null)
                {
                    // disable all other buttons
                    DeactivateReplay();
                    gestureCaptureBtn.IsEnabled = false;
                    gestureReplayBtn.IsEnabled = false;
                    previewBtn.Content = "Stop Test";
                    isStreaming = true;
                    kinect_data_manager.ifShowJointStatus = true;

                    overlap_frame_rec_buffer.Clear();

                    kinect_sensor.Start();
                }
            }
            else
            {
                if (kinect_sensor != null)
                {
                    kinect_sensor.Stop();

                    gestureCaptureBtn.IsEnabled = true;
                    gestureReplayBtn.IsEnabled = true;

                    isStreaming = false;
                    kinect_data_manager.ifShowJointStatus = false;

                    // save recorded frame to disk and save skeleton data
                    if (overlap_frame_rec_buffer!=null && skeleton_rec_buffer!=null && saveVideoCheckBox.IsChecked.Value)
                    {
                        statusbarLabel.Content = "Saving video...";

                        // create video writer
                        int fwidth = (int)groupBox3.Width + 20;
                        int fheight = (int)groupBox3.Height + 20;

                        SaveFileDialog saveDialog = new SaveFileDialog();
                        saveDialog.Filter = "avi files (*.avi)|*.avi";
                        saveDialog.FilterIndex = 2;
                        saveDialog.RestoreDirectory = true;

                        if (saveDialog.ShowDialog().Value)
                        {
                            string videofile = saveDialog.FileName.ToString();
                            //VideoWriter videoWriter = new VideoWriter(videofile, CvInvoke.CV_FOURCC('M', 'J', 'P', 'G'), 15,
                            //    fwidth, fheight, true);

                            //// save video
                            //if (videoWriter == null)
                            //    MessageBox.Show("Fail to save video. Check if codec has been installed.");
                            //else
                            //{
                            //    for (int i = 0; i < overlap_frame_rec_buffer.Count; i++)
                            //    {
                            //        // write to video file
                            //        //Emgu.CV.Image<Bgr, byte> cvImg =
                            //        //    new Emgu.CV.Image<Bgr, byte>(overlap_frame_rec_buffer[i] as Bitmap);

                            //        //videoWriter.WriteFrame<Bgr, byte>(cvImg);
                            //    }

                            //    //videoWriter.Dispose();

                                statusbarLabel.Content = "Video saved to " + videofile;
                        }

                            // save skeleton
                           // string skeletonpath = videofile + ".xml";
                            //KinectRecorder.WriteToSkeletonXMLFile(skeletonpath, skeleton_rec_buffer);
                    }

                    skeleton_rec_buffer.Clear();
                    overlap_frame_rec_buffer.Clear();

                    previewBtn.Content = "Start Test";

                    // save tracked elbow speed
                    //FileStream file = File.Open("d:\\temp\\test.txt", FileMode.Create);
                    //StreamWriter writer = new StreamWriter(file);
                    //for (int i = 0; i < motion_assessor.jointStatusSeq.Count; i++)
                    //    writer.WriteLine(motion_assessor.jointStatusSeq[i][JointType.HandRight].abs_speed);
                    //writer.Close();
                }
            }

        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // do initialization here
            gesture_recognizer = new GestureRecognizer();
            motion_assessor = new MotionAssessor();
            toMeasureUnits = new List<MeasurementUnit>();
            overlap_frame_rec_buffer = new ArrayList();
            skeleton_rec_buffer = new List<Skeleton>();
            color_frame_rec_buffer = new List<byte[]>();

            // init kinect
            if (!InitKinect())
            {
                statusbarLabel.Content = "Kinect not connected";
                MessageBox.Show("Kinect not found.");
            }
            else
                statusbarLabel.Content = "Kinect initialized";

            DeactivateReplay();

            // load gesture config and update ui
            gesture_recognizer.LoadAllGestureConfig();
            UpdateGestureComboBox();
        }

        private void measureConfigBtn_Click(object sender, RoutedEventArgs e)
        {
            MeasurementConfigWin measureConfigWin = new MeasurementConfigWin();
            //measureConfigWin.measureUnits = this.toMeasureUnits;    // restore previously selected ones
            
            if (measureConfigWin.ShowDialog().Value == true)
                toMeasureUnits = measureConfigWin.measureUnits;
            
        }

    }
}
