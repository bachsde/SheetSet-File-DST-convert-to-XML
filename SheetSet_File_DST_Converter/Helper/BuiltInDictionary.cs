﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SheetSet_File_DST_Converter.Helper
{
    public static class BuiltInDictionary
    {
        public static Dictionary<string, Dictionary<string, int>> Data = new Dictionary<string, Dictionary<string, int>>
        {
            ["xml_to_dst"] = new Dictionary<string, int>
        {
            { "60", 208 }, { "63", 205 }, { "120", 20 }, { "109", 227 }, { "108", 224 },
            { "32", 172 }, { "118", 26 }, { "101", 235 }, { "114", 30 }, { "115", 25 },
            { "105", 231 }, { "111", 29 }, { "110", 226 }, { "61", 211 }, { "34", 174 },
            { "49", 223 }, { "46", 162 }, { "48", 220 }, { "99", 233 }, { "100", 232 },
            { "103", 229 }, { "85", 251 }, { "84", 248 }, { "70", 202 }, { "45", 163 },
            { "56", 212 }, { "62", 210 }, { "13", 131 }, { "10", 134 }, { "65", 207 },
            { "83", 249 }, { "68", 200 }, { "97", 239 }, { "116", 24 }, { "98", 238 },
            { "50", 222 }, { "54", 218 }, { "67", 201 }, { "66", 206 }, { "69", 203 },
            { "52", 216 }, { "57", 215 }, { "53", 219 }, { "73", 199 }, { "55", 213 },
            { "51", 217 }, { "80", 252 }, { "112", 28 }, { "47", 221 }, { "86", 250 },
            { "82", 254 }, { "104", 228 }, { "102", 234 }, { "117", 27 }, { "95", 237 },
            { "78", 194 }, { "37", 171 }, { "76", 192 }, { "92", 240 }, { "107", 225 },
            { "119", 21 }, { "58", 214 }, { "36", 168 }, { "40", 164 }, { "79", 253 },
            { "41", 167 }, { "121", 23 }, { "88", 244 }, { "77", 195 }, { "87", 245 },
            { "106", 230 }, { "74", 198 }, { "72", 196 }, { "71", 197 }, { "75", 193 },
            { "89", 247 }, { "90", 246 }, { "122", 22 }, { "44", 160 }, { "38", 170 },
            { "59", 209 }, { "50074", 18742 }, { "14793628", 7295280 },
            { "50060", 18688 }, { "50049", 18703 }, { "14793378", 7296558 },
            { "14793404", 7296592 }, { "14793406", 7296594 }, { "14793612", 7295232 },
            { "50050", 18702 }, { "50048", 18700 }, { "50863", 19037 }, { "14793630", 7295282 },
            { "50067", 18745 }, { "14793398", 7296602 }, { "14793392", 7296604 },
            { "50320", 18492 }, { "14793610", 7295238 }, { "14793600", 7295244 },
            { "14793380", 7296552 }, { "14793382", 7296554 }, { "14793636", 7295272 },
            { "14793616", 7295292 }, { "50061", 18691 }, { "14793618", 7295294 },
            { "14793644", 7295264 }, { "14793640", 7295268 }, { "14793390", 7296546 },
            { "50058", 18694 }, { "14793624", 7295284 }, { "14793620", 7295288 },
            { "50068", 18744 }, { "50057", 18695 }, { "14793626", 7295286 },
            { "14793648", 7295324 }, { "50064", 18748 }, { "50056", 18692 },
            { "14793606", 7295242 }, { "14793602", 7295246 }, { "50077", 18739 },
            { "50306", 18446 }, { "49843", 20057 }, { "14793384", 7296548 },
            { "14793632", 7295276 }
        },

            ["dst_to_xml"] = new Dictionary<string, int>
        {
            { "208", 60 }, { "205", 63 }, { "20", 120 }, { "227", 109 }, { "224", 108 },
            { "172", 32 }, { "26", 118 }, { "235", 101 }, { "30", 114 }, { "25", 115 },
            { "231", 105 }, { "29", 111 }, { "226", 110 }, { "211", 61 }, { "174", 34 },
            { "223", 49 }, { "162", 46 }, { "220", 48 }, { "233", 99 }, { "232", 100 },
            { "229", 103 }, { "251", 85 }, { "248", 84 }, { "202", 70 }, { "163", 45 },
            { "212", 56 }, { "210", 62 }, { "131", 13 }, { "134", 10 }, { "207", 65 },
            { "249", 83 }, { "200", 68 }, { "239", 97 }, { "24", 116 }, { "238", 98 },
            { "222", 50 }, { "218", 54 }, { "201", 67 }, { "206", 66 }, { "203", 69 },
            { "216", 52 }, { "215", 57 }, { "219", 53 }, { "199", 73 }, { "213", 55 },
            { "217", 51 }, { "252", 80 }, { "28", 112 }, { "221", 47 }, { "250", 86 },
            { "254", 82 }, { "228", 104 }, { "234", 102 }, { "27", 117 }, { "237", 95 },
            { "194", 78 }, { "171", 37 }, { "192", 76 }, { "240", 92 }, { "225", 107 },
            { "21", 119 }, { "214", 58 }, { "168", 36 }, { "164", 40 }, { "253", 79 },
            { "167", 41 }, { "23", 121 }, { "244", 88 }, { "195", 77 }, { "245", 87 },
            { "230", 106 }, { "198", 74 }, { "196", 72 }, { "197", 71 }, { "193", 75 },
            { "247", 89 }, { "18742", 50074 }, { "7295280", 14793628 }, { "18688", 50060 },
            { "18703", 50049 }, { "7296558", 14793378 }, { "7296592", 14793404 },
            { "7296594", 14793406 }, { "7295232", 14793612 }, { "18702", 50050 },
            { "18700", 50048 }, { "19037", 50863 }, { "7295282", 14793630 },
            { "18745", 50067 }, { "7296602", 14793398 }, { "7296604", 14793392 },
            { "18492", 50320 }, { "7295238", 14793610 }, { "7295244", 14793600 },
            { "7296552", 14793380 }, { "7296554", 14793382 }, { "7295272", 14793636 },
            { "246", 90 }, { "7295292", 14793616 }, { "18691", 50061 }, { "7295294", 14793618 },
            { "7295264", 14793644 }, { "7295268", 14793640 }, { "7296546", 14793390 },
            { "18694", 50058 }, { "7295284", 14793624 }, { "7295288", 14793620 },
            { "18744", 50068 }, { "18695", 50057 }, { "7295286", 14793626 },
            { "7295324", 14793648 }, { "18748", 50064 }, { "160", 44 }, { "170", 38 },
            { "209", 59 }, { "18692", 50056 }, { "7295242", 14793606 }, { "7295246", 14793602 },
            { "22", 122 }, { "18739", 50077 }, { "18446", 50306 }, { "20057", 49843 },
            { "7296548", 14793384 }, { "7295276", 14793632 }
        }
        };
    }

}
