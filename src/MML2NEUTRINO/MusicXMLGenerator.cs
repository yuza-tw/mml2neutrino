using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace MML2NEUTRINO
{
    public class MusicXMLGenerator
    {
        // 16分音符と8分音符3連符をサポート → 全音符 = 48
        int[] durations = new int[] { 0,
            48,24,16,12, 0,8,0,6, 0,0,0,4, 0,0,0,3,
            0,0,0,0, 0,0,0,2, 0,0,0,0, 0,0,0,0,
            0,0,0,0, 0,0,0,0, 0,0,0,0, 0,0,0,1 }; // L1=48,L2=24,L3=16,L4=12,L6=8,L8=6,L12=4,L16=3.L24=2,L48=1 

        enum TieType {
            NotTied = 0,
            TieStart,
            TieEnd
        };

        int mDuration = 0;
        const int maxDuration = 48;
        XElement xml = null;
        XElement part = null;
        int bar = 1;

        public MusicXMLGenerator()
        {
        }

        private void Initialize()
        {
            // Read template from resource
            System.Reflection.Assembly myAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            string[] resnames = myAssembly.GetManifestResourceNames();
            using (StreamReader sr = new StreamReader(myAssembly.GetManifestResourceStream("MML2NEUTRINO.template.xml"), Encoding.UTF8))
            {
                xml = XElement.Load(sr);
            }
            part = (
                    from e in xml.Elements("part")
                    select e).Last();
        }

        public XElement GenerateFromElements(IElement[] elements)
        {
            Initialize();

            bar = 1;
            XElement measure = new XElement("measure");
            measure.Add(Attribute(maxDuration/4, 0, 4, 4, "G", 2));
            measure.SetAttributeValue("number", bar++);

            foreach (var elem in elements)
            {
                switch(elem)
                {
                    case Note note:
                        measure = CreateNote(measure, note);
                        break;
                    case Rest rest:
                        measure = CreateRest(measure, rest);
                        break;
                    case Tempo tempo:
                        measure = CreateTempo(measure, tempo);
                        break;
                }
            }

            if (mDuration > 0)
            {
                measure = CreateRest(measure, new Rest { Length = maxDuration - mDuration, HasDot = false });
                part.Add(measure);
            }
            return xml;
        }

        private string CreateMML(IElement[] elements, int i)
        {
            StringBuilder sb = new StringBuilder();
            for (int j = 0; j < elements.Length && j <= i; j++)
            {
                sb.Append(elements[j].MML);
            }
            string mml = sb.ToString();
            return mml;
        }

        public XElement CreateTempo(XElement measure, Tempo tempo)
        {
            XElement sound = new XElement("sound");
            sound.SetAttributeValue("tempo", tempo.Value);
            measure.Add(new XElement("direction",
                new XElement("direction-type",
                    new XElement("metronome",
                        new XElement("beat-unit", "quater"),
                        new XElement("per-minute", tempo.Value))),
                sound)
            );
            return measure;
        }
        private XElement Attribute(int divisions, int fifths, int beats, int beatType, string sign, int line)
        {
            XElement attribute = new XElement("attributes",
                new XElement("divisions", divisions),
                new XElement("key",
                    new XElement("fifths", fifths)),
                new XElement("time",
                    new XElement("beats", beats),
                    new XElement("beat-type", beatType)),
                new XElement("clef",
                    new XElement("sign", sign),
                    new XElement("line", line))
                );
            return attribute;
        }

        private XElement CreateNote(XElement measure, Note n)
        {
            if(n.Length<0 || n.Length>= durations.Length || durations[n.Length] == 0)
            {
                throw new FormatException($"長さ {n.Length} は指定できません。");
            }

            int duration = durations[n.Length];
            if (n.HasDot)
            {
                duration = duration * 3 / 2;
            }

            bool tied = false;

            while (mDuration + duration > maxDuration)
            {
                if (mDuration < maxDuration)
                {
                    // tied note
                    int rest = maxDuration - mDuration;
                    measure.Add(NoteElem(n, rest, TieType.TieStart));
                    duration -= rest;
                    tied = true;
                }
                part.Add(measure);
                measure = new XElement("measure");
                measure.SetAttributeValue("number", bar++);
                mDuration = 0;
            }

            measure.Add(NoteElem(n, duration, tied ? TieType.TieEnd : TieType.NotTied));
            mDuration += duration;

            return measure;
        }

        private XElement NoteElem(Note n, int duration, TieType tieType)
        {
            var note = new XElement("note",
                new XElement("pitch",
                    new XElement("step", n.Step),
                    new XElement("alter", n.Alter),
                    new XElement("octave", n.Octave)
                ),
                new XElement("duration", duration)
            );

            string lyric = n.Lyric;

            if (tieType != TieType.NotTied)
            {
                string[] kTieTypeString = { "", "start", "stop" };

                XElement tieElement = new XElement("tie");
                tieElement.SetAttributeValue("type", kTieTypeString[(int)tieType]);
                note.Add(tieElement);

                XElement tiedElement = new XElement("tied");
                tiedElement.SetAttributeValue("type", kTieTypeString[(int)tieType]);

                XElement notationElement = new XElement("notations", tiedElement);
                note.Add(notationElement);
            }

            if (lyric.Last() == ',')
            {
                lyric = lyric.TrimEnd(',');
                if (tieType != TieType.TieStart)
                {
                    AddBreath(note);
                }
            }

            if (tieType != TieType.TieEnd)
            {
                XElement lyricElement = new XElement("lyric", new XElement("text", lyric));
                note.Add(lyricElement);
            }

            return note;
        }
        
        private void AddBreath(XElement note)
        {
            note.Add(
                new XElement("notations",
                new XElement("articulations",
                new XElement("breath-mark"))));
        }


        private XElement RestElem(int duration)
        {
            return new XElement("note",
                new XElement("rest"),
                new XElement("duration", duration)
            );
        }

        private XElement CreateRest(XElement measure, Rest r)
        {
            int duration = durations[r.Length];
            if (r.HasDot)
            {
                duration = duration * 3 / 2;
            }

            int spill = mDuration + duration - maxDuration;
            if (spill > 0)
            {
                bool newBar = (mDuration == maxDuration);
                if (!newBar)
                {
                    int rest = maxDuration - mDuration;
                    measure.Add(RestElem(rest));
                    duration -= rest;
                }
                part.Add(measure);
                measure = new XElement("measure");
                measure.SetAttributeValue("number", bar++);
                mDuration = 0;
            }

            measure.Add(RestElem(duration));
            mDuration += duration;

            return measure;
        }
    }
}
