﻿using LiteDB;
using LiteDB.Engine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LiteDB.Demo
{
    public class HtmlDump
    {
        private readonly BsonDocument _page;
        private readonly uint _pageID;
        private readonly PageType _pageType;
        private readonly byte[] _buffer;
        private readonly StringBuilder _writer = new StringBuilder();
        private readonly List<PageItem> _items = new List<PageItem>();
        private string[] _colors = new string[] {
            /* 400 */ "#B2EBF2", "#FFECB3"
        };

        public HtmlDump(BsonDocument page)
        {
            _page = page;
            _buffer = page["buffer"].AsBinary;
            _pageID = BitConverter.ToUInt32(_buffer, 0);
            _pageType = (PageType)_buffer[4];
            page.Remove("buffer");

            this.LoadItems();
            this.ReadItems();
        }

        private void LoadItems()
        {
            for (var i = 0; i < _buffer.Length; i++)
            {
                _items.Add(new PageItem
                {
                    Index = i,
                    Value = _buffer[i],
                    Title = "#" + i,
                    Color = -1
                });
            }
        }

        private void ReadItems()
        {
            // some span for header
            spanPageID(0, "PageID");
            spanItem<byte>(4, 0, null, "PageType", null);
            spanPageID(5, "PrevPageID");
            spanPageID(9, "NextPageID");

            spanItem(13, 3, null, "TransactionID", BitConverter.ToUInt32);
            spanItem<byte>(17, 0, null, "IsConfirmed", null);
            spanItem(18, 3, null, "ColID", BitConverter.ToUInt32);

            spanItem<byte>(22, 0, null, "ItemsCount", null);
            spanItem(23, 1, null, "UsedBytes", BitConverter.ToUInt16);
            spanItem(25, 1, null, "FragmentedBytes", BitConverter.ToUInt16);
            spanItem(27, 1, null, "NextFreePosition", BitConverter.ToUInt16);
            spanItem<byte>(29, 0, null, "HighestIndex", null);
            spanItem<byte>(30, 0, null, "Reserved", null);
            spanItem<byte>(31, 0, null, "Reserved", null);

            // color segments
            var highestIndex = _buffer[29];

            if (highestIndex < byte.MaxValue)
            {
                var colorIndex = 0;

                for(var i = 0; i <= highestIndex; i++)
                {
                    var posAddr = _buffer.Length - ((i + 1) * 4) + 2;
                    var lenAddr = _buffer.Length - (i + 1) * 4;

                    var position = BitConverter.ToUInt16(_buffer, posAddr);
                    var length = BitConverter.ToUInt16(_buffer, lenAddr);

                    _items[lenAddr].Span = 3;
                    _items[lenAddr].Text = $"{i}: {position} ({length})";
                    _items[lenAddr].Href = "#b" + position;

                    if (position != 0)
                    {
                        _items[posAddr].Color = _items[lenAddr].Color = (colorIndex++ % _colors.Length);

                        _items[position].Id = "b" + position;

                        if (_pageType == PageType.Data)
                        {
                            spanPageID(position + 1, "NextBlockID");
                        }

                        for (var j = position; j < position + length; j++)
                        {
                            _items[j].Color = colorIndex - 1;
                        }
                    }
                }
            }

            // fixing zebra segment colors
            var current = 0;
            var color = 0;

            for(var i = 0; i < _buffer.Length; i++)
            {
                if (_items[i].Color != -1)
                {
                    if (_items[i].Color != current)
                    {
                        color++;
                    }

                    current = _items[i].Color;
                    _items[i].Color = color % _colors.Length;
                }
            }

            if (_pageType == PageType.Header)
            {
                spanItem(32, 26, null, "HeaderInfo", (byte[] b, int i) => Encoding.UTF8.GetString(b, i, 27));
                spanItem<byte>(59, 0, null, "FileVersion", null);
                spanPageID(60, "FreeEmptyPageID");
                spanPageID(64, "LastPageID");
                spanItem(68, 7, null, "CreationTime", (byte[] b, int i) => new DateTime(BitConverter.ToInt64(b, i)).ToString("o"));
                spanItem(76, 3, null, "UserVersion", BitConverter.ToInt32);
            }

            void spanItem<T>(int index, int span, string href, string title, Func<byte[], int, T> convert)
            {
                _items[index].Span = span;
                _items[index].Title = title;
                _items[index].Text = convert == null ? _items[index].Value.ToString() : convert(_buffer, index).ToString();
                _items[index].Href = href?.Replace("{text}", _items[index].Text);
            }

            void spanPageID(int index, string title)
            {
                var pageID = BitConverter.ToUInt32(_buffer, index);

                _items[index].Span = 3;
                _items[index].Title = title;
                _items[index].Text = pageID.ToString();
                _items[index].Href = pageID == uint.MaxValue ? null : "/" + pageID;
            }
        }

        public string Render()
        {
            if (_page == null) return "Page not found";

            this.RenderHeader();
            this.RenderInfo();
            this.RenderConvert();
            this.RenderPage();
            this.RenderFooter();

            return _writer.ToString();
        }

        private void RenderHeader()
        {
            _writer.AppendLine("<html>");
            _writer.AppendLine("<head>");
            _writer.AppendLine($"<title>LiteDB Database Debugger: {_pageID} - {_pageType}</title>");
            _writer.AppendLine("<style>");
            _writer.AppendLine("textarea { margin: 0px; width: 819px; height: 61px; vertical-align: top; }");
            _writer.AppendLine(".page { display: flex; flex-wrap: wrap; width: 1205px; }");
            _writer.AppendLine(".page > a { font-family: monospace; background-color: #d1d1d1; margin: 1px; width: 60px; flex-basis: 35px; text-align: center; padding: 5px 0; position: relative; }");
            _writer.AppendLine(".page > a:before { font-family: arial; font-size: 7px; color: gray; content: attr(i); position: absolute; left: 0px; top: -1px; background-color: white; padding-right: 2px; }");

            foreach (var color in _items.Select(x => x.Color).Where(x => x != -1).Distinct())
            {
                _writer.AppendLine($".c{color} {{ background-color: {_colors[color % _colors.Length]} !important; }}");
            }

            _writer.AppendLine("</style>");
            _writer.AppendLine("</head>");
            _writer.AppendLine("<body>");
            _writer.AppendLine($"<h1>{_pageType} - #{_pageID.ToString().PadLeft(4, '0')}</h1><hr/>");
        }

        private void RenderInfo()
        {
            _writer.AppendLine("<pre>");

            var json = new JsonWriter(new StringWriter(_writer));
            json.Pretty = true;
            json.Indent = 4;
            json.Serialize(_page);

            _writer.AppendLine("</pre>");
        }

        private void RenderConvert()
        {
            _writer.AppendLine($"<form method='post' action='/{_pageID}'>");
            _writer.AppendLine("<textarea placeholder='Place hex page content' name='b'></textarea>");
            _writer.AppendLine("<button type='submit'>Paste</button>");
            _writer.AppendLine("</form>");
        }

        private void RenderPage()
        {
            _writer.AppendLine("<div class='page'>");

            var position = 0;

            for(var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];

                _writer.Append($"<a");

                if (position % 32 == 0)
                {
                    _writer.AppendLine($" i={i}");
                }

                if (!string.IsNullOrEmpty(item.Href))
                {
                    _writer.Append($" href='{item.Href}'");
                }

                if (item.Color >= 0)
                {
                    _writer.Append($" class='c{item.Color}'");
                }

                if (item.Span > 0)
                {
                    _writer.Append($" style='flex-basis: {35 * (item.Span + 1) + (item.Span * 2)}px'");
                }

                if (!string.IsNullOrEmpty(item.Title))
                {
                    _writer.Append($" title='{item.Title}'");
                }

                if (!string.IsNullOrEmpty(item.Id))
                {
                    _writer.Append($" id='{item.Id}'");
                }

                _writer.Append(">");
                _writer.Append(item.Text ?? item.Value.ToString());
                _writer.Append("</a>");

                position += (item.Span + 1);
                i += item.Span;
            }

            _writer.AppendLine("</div>");
        }

        private void RenderFooter()
        {
            _writer.AppendLine("</body>");
            _writer.AppendLine("</html>");
        }

        public class PageItem
        {
            public int Index { get; set; }
            public string Href { get; set; }
            public string Id { get; set; }
            public string Title { get; set; }
            public string Text { get; set; }
            public byte Value { get; set; }
            public int Span { get; set; }
            public int Color { get; set; }
        }
    }
}
