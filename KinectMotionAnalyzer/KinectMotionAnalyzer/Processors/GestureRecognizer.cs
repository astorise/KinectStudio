﻿using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Kinect;


namespace KinectMotionAnalyzer.Processors
{

    enum GestureName
    {
        Unknown,
        Bicep_Curl,
        Squat,
        Shoulder_Press
    }

    /// <summary>
    /// user gesture
    /// </summary>
    class Gesture
    {
        // actual gesture data
        public List<Skeleton> data = new List<Skeleton>();
        public GestureName name = GestureName.Unknown;
    }

    /// <summary>
    /// template gesture
    /// </summary>
    class GestureTemplate
    {
        // actual gesture data
        public List<Skeleton> data = new List<Skeleton>();
        public GestureName name = GestureName.Unknown;

        // weight for each joint used in recognition (matching)
        public Dictionary<JointType, float> jointWeights = new Dictionary<JointType, float>();
    }


    /// <summary>
    /// recognizer based on dtw
    /// </summary>
    class GestureRecognizer
    {

        private Dictionary<string, GestureName> GESTURE_DICT = new Dictionary<string, GestureName>();

        private Dictionary<GestureName, List<GestureTemplate>> GESTURE_DATABASE = 
            new Dictionary<GestureName, List<GestureTemplate>>();

        public int gesture_min_len = 0;
        public int gesture_max_len = 0;

        public GestureRecognizer()
        {
            // set up gesture type mapping
            GESTURE_DICT.Add("Unknown", GestureName.Unknown);
            GESTURE_DICT.Add("Bicep_Curl", GestureName.Bicep_Curl);
            GESTURE_DICT.Add("Squat", GestureName.Squat);
            GESTURE_DICT.Add("Shoulder_Press", GestureName.Shoulder_Press);
        }


        /// <summary>
        /// read database data from files
        /// </summary>
        public bool LoadGestureDatabase(string database_dir)
        {
            // enumerate gesture directories
            if (!Directory.Exists(database_dir))
                return false;

            IEnumerable<string> gesture_dirs = Directory.EnumerateDirectories(database_dir);
            int g_min_len = int.MaxValue;
            int g_max_len = int.MinValue;
            foreach (string gdir in gesture_dirs)
            {
                // get dir name
                int slash_id = gdir.LastIndexOf('\\');
                string dirname = gdir.Substring(slash_id+1, gdir.Length - slash_id-1);
                if (!GESTURE_DICT.ContainsKey(dirname))
                    continue;

                List<GestureTemplate> cur_gestures = new List<GestureTemplate>();
                IEnumerable<string> gesture_files = Directory.EnumerateFiles(gdir);
                foreach (string filename in gesture_files)
                {
                    GestureTemplate gtemp = new GestureTemplate();
                    gtemp.data = KinectRecorder.ReadFromSkeletonFile(filename);
                    gtemp.name = GESTURE_DICT[dirname];

                    if (gtemp.data.Count > g_max_len)
                        g_max_len = gtemp.data.Count;
                    if (gtemp.data.Count < g_min_len)
                        g_min_len = gtemp.data.Count;

                    cur_gestures.Add(gtemp);
                }

                // add to database
                GESTURE_DATABASE.Add(GESTURE_DICT[dirname], cur_gestures);
            }

            return true;
        }


        /// <summary>
        /// match to each database template
        /// </summary>
        public float MatchToDatabase(Gesture input)
        {
            // find the most similar gesture in database to test gesture
            float mindist = float.PositiveInfinity;
            GestureName bestname = GestureName.Unknown;
            foreach (GestureName gname in GESTURE_DATABASE.Keys)
            {
                foreach (GestureTemplate temp in GESTURE_DATABASE[gname])
                {
                    float dist = GestureSimilarity(input, temp);
                    if(dist < mindist)
                    {
                        mindist = dist;
                        bestname = gname;
                    }
                }
            }

            return mindist;
        }


        /// <summary>
        /// measure similarity between input gesture and a gesture template
        /// </summary>
        public float GestureSimilarity(Gesture input, GestureTemplate template)
        {
            // normalize to hip center
            for (int i = 0; i < input.data.Count; i++)
            {
                Skeleton ske = input.data[i];

                // subtract each joint position with hip center position
                SkeletonPoint hipcenter = ske.Joints[JointType.HipCenter].Position;
                SkeletonPoint point = new SkeletonPoint();
                foreach (Joint joint in ske.Joints)
                {
                    Joint tjoint = joint;
                    point.X = joint.Position.X - hipcenter.X;
                    point.Y = joint.Position.Y - hipcenter.Y;
                    point.Z = joint.Position.Z - hipcenter.Z;
                    tjoint.Position = point;
                    ske.Joints[joint.JointType] = tjoint;
                }

                input.data[i] = ske;
            }

            float dist = DynamicTimeWarping(input.data, template.data, template.jointWeights);

            return 0;
        }


        /// <summary>
        /// generic dtw algorithm
        /// </summary>
        public float DynamicTimeWarping(
            Dictionary<int, Skeleton> input1, Dictionary<int, Skeleton> input2, Dictionary<JointType, float> weights)
        {
            if (input1 == null || input2 == null)
                return -1;

            // perform DTW to align two arrays
            int length1 = input1.Count;
            int length2 = input2.Count;
            float[][] DTW = {new float[length1+1], new float[length2+1]};   // make an extra space for 0 match

            for(int i=1; i<=length1; i++)
                DTW[0][i] = float.PositiveInfinity;
            for(int i=1; i<=length2; i++)
                DTW[i][0] = float.PositiveInfinity;
            DTW[0][0] = 0;

            for(int i=1; i<=length1; i++)
            {
                for(int j=1; j<=length2; j++)
                {
                    int loc1 = i - 1;
                    int loc2 = j - 1;
                    float cost = DistBetweenPose(input1[loc1], input2[loc2], weights);
                    DTW[i][j] = cost + Math.Min(DTW[i-1][j], Math.Min(DTW[i][j-1], DTW[i-1][j-1]));
                }
            }

            return DTW[length1][length2];
        }

        private float DistBetweenPose(Skeleton pose1, Skeleton pose2, Dictionary<JointType, float> weights)
        {
            if (pose1 == null || pose2 == null)
                return -1;

            float dist = 0;
            float sumw = 0;
            for (int i = 0; i < pose1.Joints.Count; i++)
            {
                JointType type = (JointType)i;
                float dx = pose1.Joints[type].Position.X - pose2.Joints[type].Position.X;
                float dy = pose1.Joints[type].Position.Y - pose2.Joints[type].Position.Y;
                float dz = pose1.Joints[type].Position.Z - pose2.Joints[type].Position.Z;
                float pointdist = dx * dx + dy * dy + dz * dz;
                pointdist = (float)Math.Sqrt((double)pointdist);

                dist += weights[type] * pointdist;
                sumw += weights[type];
            }

            dist /= sumw;

            return dist;
        }
    }
}
