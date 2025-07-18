﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace NAPS2.Scan.Wia.Native
{
    /// <summary>
    /// Property value constants.
    /// </summary>
    public static class WiaPropertyValue
    {
        public const int FEEDER = 1;
        public const int FLATBED = 2;
        public const int DUPLEX = 4;
        public const int FRONT_FIRST = 8;
        public const int BACK_FIRST = 16;
        public const int FRONT_ONLY = 32;
        public const int BACK_ONLY = 64;
        public const int ADVANCED_DUPLEX = 1024;
        public const int AUTO_DESKEW_ON = 0;
        public const int AUTO_DESKEW_OFF = 1;
       
        public const int MULTI_FEED_DETECT_DISABLED = 0;
        public const int MULTI_FEED_DETECT_STOP_ERROR = 1;
        public const int MULTI_FEED_DETECT_STOP_SUCCESS = 2;
        public const int MULTI_FEED_DETECT_CONTINUE = 3;

        public const int FEED_READY = 1;
    }
}