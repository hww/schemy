using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using Code.DataStructures;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Code.DataStructures;
using UnityEngine.Experimental.Rendering.HDPipeline;

namespace Schemy
{
	/// <summary>
	/// 22.2.1.1 Dynamic Control of the Arrangement of Output
	/// 
	/// В первой строке следующего рисунка показан схематический вывод. Каждый из символов на выходе
	/// представлен «-». Позиции условных строк новой строки обозначаются цифрами. Начало и конец
	/// логических блоков обозначаются символами { и } соответственно.
	/// 
	///  {-1---{--{--2---3-}--4--}-}
	///  000000000000000000000000000     
	///  11 111111111111111111111111     
	///            22 222                
	///               333 3333           
	///         44444444444444 44444     
	/// 
	/// Вывод в целом является логическим блоком и самой внешней секцией. Этот раздел обозначается
	/// цифрами 0 во второй строке на рисунке. Логические блоки, вложенные в выход, задаются макросом
	/// pprint-логическим блоком. Условные позиции новой строки определяются вызовами pprint-newline.
	/// Каждая условная строка новой строки определяет два раздела (один перед ним и один за ним) и
	/// связан с третьим (раздел, непосредственно содержащий его).
	/// 
	/// Секция после условной новой строки состоит из: всего вывода до, но не включая:
	/// 
	///      (a) следующую условную новую строку, содержащуюся в том же логическом блоке; 
	///      (б) следующая новая строка, которая находится на меньшем уровне вложенности
	///          в логические блоки; 
	///      (c) конец вывода.
	/// 
	/// Секция перед условной новой строкой состоит из: всего вывода до неё, но не включая:
	/// 
	///      (a) предыдущую условную новую строку, которая немедленно содержится в одном
	///      логическом блоке; 
	///      (b) начало немедленно содержащего логический блок.
	/// 
	/// Последние четыре строки на рисунке показывают разделы до и после четырех условных строк
	/// новой строки.
	/// </summary>
	public static class PrettyPriner
	{
		// =============================================================================================================
		// DISPATCHNG TABLE
		// =============================================================================================================
		/// <summary>
		/// Delegate which render list to givent stream
		/// </summary>
		public delegate void LogicalBlockDelegate(PrettyStream stream, List<object> list);

		/// <summary>
		/// Dictionary which contains delegates per each kind of expression
		/// </summary>
		private static Dictionary<Symbol, LogicalBlockDelegate> PrintpprintDispatch =
			new Dictionary<Symbol, LogicalBlockDelegate>();

		// =============================================================================================================
		// CONFIGURATIONS & SETTINGS
		// =============================================================================================================		

		public const int DEFAULT_BUFFER_SIZE = 256;
		
		/// <summary>
		/// If it is non-nil, it specifies the right margin (as integer number of ems) to use
		/// when the pretty printer is making layout decisions. 
		/// </summary>
		public static int PrintRightMargin = 80;

		/// <summary>
		/// Controls the format in which arrays are printed. If it is false, the contents of arrays
		/// other than strings are never printed. Instead, arrays are printed in a concise form
		/// using #<> that gives enough information for the user to be able to identify the array,
		/// but does not include the entire array contents. If it is true, non-string arrays are
		/// printed using #(...), #*, or #nA syntax. 
		/// </summary>
		public static bool PrintArray = true;

		public static bool Printbase = true;

		public enum EPrintCase
		{
			Default,
			Upcase,
			Downcase,
			Capitalize
		}

		/// <summary>
		/// Case conversion for printer
		/// </summary>
		public static EPrintCase PrintCase = EPrintCase.Default;

		/// <summary>
		/// If false, escape characters and package prefixes are not output when an expression is printed.
		/// If true, an attempt is made to print an expression in such a way that it can be read again
		/// to produce an equal expression. (This is only a guideline; not a requirement. See *print-readably*.) 
		/// </summary>
		public static bool PrintEscape = true;

		/// <summary>
		/// Controls whether the prefix "#:" is printed before apparently uninterned symbols.
		/// The prefix is printed before such symbols if and only if the value of *print-gensym* is true. 
		/// </summary>
		public static bool PrintgenSym = true;

		/// <summary>
		/// controls how many levels deep a nested object will print. If it is false, then no control
		/// is exercised. Otherwise, it is an integer indicating the maximum level to be printed.
		/// An object to be printed is at level 0; its components (as of a list or vector) are at
		/// level 1; and so on. If an object to be recursively printed has components and is at a level
		/// equal to or greater than the value of *print-level*, then the object is printed as "#". 
		/// </summary>
		public static int Printlevel = 0;

		/// <summary>
		/// controls how many elements at a given level are printed. If it is false, there is no
		/// limit to the number of components printed. Otherwise, it is an integer indicating
		/// the maximum number of elements of an object to be printed. If exceeded,
		/// the printer will print "..." in place of the other elements. In the case of a
		/// dotted list, if the list contains exactly as many elements as the value
		/// of *print-length*, the terminating atom is printed rather than printing "..." 
		/// </summary>
		public static int PrintLength = 0;

		/// <summary>
		/// When the value of *print-lines* is other than nil, it is a limit on the number of output
		/// lines produced when something is pretty printed. If an attempt is made to go beyond
		/// that many lines, ".." is printed at the end of the last line followed by all of the
		/// suffixes (closing delimiters) that are pending to be printed. 
		/// </summary>
		public static int PrintLines = 0;

		/// <summary>
		/// If it is not 0, the pretty printer switches to a compact style of output (called miser style)
		/// whenever the width available for printing a substructure is less than or equal to this many ems. 
		/// </summary>
		public static int PrintmiserWidth = 0;
		
		/// <summary>
		/// Controls whether the Lisp printer calls the pretty printer. 
		/// </summary>
		public static bool Printpretty = true;

		/// <summary>
		/// If *print-readably* is true, some special rules for printing objects go into effect. Specifically,
		/// printing any object O1 produces a printed representation that, when seen by the Lisp reader while
		/// the standard readtable is in effect, will produce an object O2 that is similar to O1. The printed
		/// representation produced might or might not be the same as the printed representation produced
		/// when *print-readably* is false. If printing an object readably is not possible, an error of type
		/// print-not-readable is signaled rather than using a syntax (e.g., the "#<" syntax) that would not
		/// be readable by the same implementation. If the value of some other printer control variable is
		/// such that these requirements would be violated, the value of that other variable is ignored. 
		/// </summary>
		public static bool PrintReadably;

		// =============================================================================================================
		// QUEUE OPERATIONS
		// =============================================================================================================
		/// <summary>All pretty print commands innerinced from this class</summary>
		public abstract class QueuedOp
		{
			/// <summary>Start column of this operation </summary>
			public int Column;
		}
		/// <summary>Simple string form</summary>
		public sealed class StringOp : QueuedOp
		{
			public string Str;
			public int Length => Str.Length;
		}
		/// <summary>Identination command</summary>
		public sealed class Identination : QueuedOp
		{
			public enum EKind { Block, Curent }
			/// <summary>The kins of this identination Block, Curent</summary>
			public EKind Kind;
			/// <summary>How many character we should add to modify indentination can be negative value</summary>
			public int Amount;
		}
		/// <summary>Tabulator</summary>
		public sealed class Tab : QueuedOp
		{
			public enum EKind { Line, LineRelative, Section, SectionRelative }
			public bool IsSection;
			public bool IsRelative;
			public int ColNum;
			public int ColInc;
		}		
		/// <summary>Start of new section</summary>
		public abstract class SectionStart : QueuedOp
		{
			/// <summary>Depth of this secton</summary>
			public int Depth;
			/// <summary>Where is the end of this section</summary>
			public QueuedOp SectionEnd;
		}
		/// <summary>Conditional new line</summary>
		public sealed class NewLine : SectionStart
		{
			public enum EKind { Linear, Fill, Mantadory }
			public EKind Kind;
		}
		/// <summary>Start of the logcal block</summary>
		public sealed class BlockStart : SectionStart
		{
			public string Prefix;
			public string Suffix;
			public BlockEnd BlockEnd; // null or block end
		}
		/// <summary>End of the logical block</summary>
		public sealed class BlockEnd : QueuedOp
		{
			public string Suffix;
		}
		// =============================================================================================================
		// LOGICAL BLOCK
		// =============================================================================================================
		public class LogicalBlock 
		{
			///<summary> The column this logical block started in.</summary>
			public int StartColumn = 0;
			///<summary> The column the current section started in.</summary>
			public int SectionColumn = 0;
			///<summary> The line number </summary>
			public int SectionStartLine = 0;			
			///<summary> The length of the per-line prefix.  We can't move the indentation left of this.</summary>
			public int PerLinePrefixEnd = 0;
			///<summary> The overall length of the prefix, including any indentation.</summary>
			public int PrefixLength = 0;
			///<summary> The overall length of the suffix.</summary>
			public int SuffixLength = 0;
			///<summary> Index used by PrintPop instruction </summary>
			private int ListIndex;
			///<summary> The list used by PrintPop instruction </summary>
			private readonly List<object> List;
			///<summary> Create new logical block </summary>
			public LogicalBlock()
			{
				List = null;
				ListIndex = 0;
			}
			///<summary> Create new logical block </summary>
			public LogicalBlock(List<object> list)
			{
				if (list == null)
					throw new ArgumentNullException();
				List = list;
				ListIndex = 0;
			}
			///<summary> Pop next element from th logical block's list</summary>
			public object PrintPop() {
				if (ListIndex < List.Count)
					return List[ListIndex++];
				throw new ArgumentNullException();
			}
			///<summary> Check if list is empty</summary>
			public bool IsListExhausted() { return ListIndex >= List.Count; }
		}

		// =============================================================================================================
		// PRETTY PRINT STREAM
		// =============================================================================================================

		public sealed class PrettyStream
		{
			/// <summary>Where the output is going to finally go.</summary>
			private readonly StreamWriter Stream;
			/// <summary>A simple string holding all the text that has been output but not yet printed</summary>
			private readonly StringBuilder Buffer;
			/// <summary>Stack of logical blocks in effect at the buffer start.</summary>
			private readonly DLinkedList<LogicalBlock> Blocks;
			/// <summary>Block-start queue entries in effect at the queue head.</summary>
			private readonly DLinkedList<LogicalBlock> PenddingBlocks;
			/// <summary>Current column of the stream</summary>
			public int Column;
			/// <summary>
			/// The line number we are currently on.  Used for *print-lines* abrevs and
			/// to tell when sections have been split across multiple lines.
			/// </summary>
			public int LineNumber;
			/// <summary>Curent indentinaton</summary>			
			public int Identination;
			/// <summary>
			/// Buffer holding the per-line prefix active at the buffer start.
			/// Indentation is included in this.  The length of this is stored
			/// in the logical block stack.
			/// </summary>
			public char[] Prefix = new char[DEFAULT_BUFFER_SIZE];
			/// <summary>
			/// Buffer holding the total remaining suffix active at the buffer start.
			/// The characters are right-justified in the buffer to make it easier
			/// to output the buffer.  The length is stored in the logical block
			/// stack.
			/// </summary>
			public char[] Sufix = new char[DEFAULT_BUFFER_SIZE];
			
			///=========================================================================================================
			/// Properties
			///=========================================================================================================

			/// <summary>Get curent block</summary>
			public LogicalBlock LogicalBlock => Blocks.Last.Value;
			/// <summary>Remove top element of the stack</summary>
			public LogicalBlock PopLogicalBlock()
			{
				var item = Blocks.Last;
				item.Remove(); 
				return item.Value; 
			}
			/// <summary>Add top element of the stack</summary>
			private void PushLogicalBlock(LogicalBlock block) { Blocks.AddLast(block); }
			///=========================================================================================================
			/// Constructors
			///=========================================================================================================

			public PrettyStream(StreamWriter stream)
			{
				Stream = stream;
				Buffer = new StringBuilder();
				QueueList = new DLinkedList<QueuedOp>();
				Blocks = new DLinkedList<LogicalBlock>();
				PenddingBlocks = new DLinkedList<LogicalBlock>();
				LogicalBlock block = new LogicalBlock();
				PushLogicalBlock(block);
			}

			///=========================================================================================================
			/// The pending operation queue
			///=========================================================================================================
			/// <summary>
			/// Queue of pending operations.  When empty, HEAD=TAIL=NIL.  Otherwise,
			/// TAIL holds the first (oldest) cons and HEAD holds the last (newest)
			/// cons.  Adding things to the queue is basically (setf (cdr head) (list
			/// new)) and removing them is basically (pop tail) [except that care must
			/// be taken to handle the empty queue case correctly.]
			/// </summary>
			private readonly DLinkedList<QueuedOp> QueueList;
			/// <summary>Enqueue one operation. Before that enqueue also all printed string</summary>
			private void Enqueue	(QueuedOp operation)
			{
				if (Buffer.Length > 0)
				{
					var stringOperation = new StringOp();
					stringOperation.Column = Column;
					stringOperation.Str = Buffer.ToString();
					QueueList.AddLast(stringOperation);
				}
				QueueList.AddLast(operation);
				Buffer.Clear();
			}
			/// <summary>Enqueue new line</summary>
			private void EnqueueNewLine(NewLine.EKind kind)
			{
				var dept = PenddingBlocks.Count;
				var newline = new NewLine();
				newline.Kind = kind;
				newline.Depth = dept;
				Enqueue(newline);
				// find when section start of same depth, and set it's section end
				var current = QueueList.Last;
				while (current!=null)
				{
					var sectionStart = current.Value as SectionStart;
					if (sectionStart==null) continue;
					if (sectionStart != newline && dept == sectionStart.Depth && sectionStart.SectionEnd == null)
					{
						sectionStart.SectionEnd = new NewLine();
						break;
					}
					current = current.Previous;
				}

				MaybeOuput(kind == NewLine.EKind.Mantadory);
			}
			/// <summary>Enqueue singe identination</summary>
			private void EnqueueIndent(Identination.EKind kind, int amount)
			{
				var identination = new Identination();
				identination.Kind = kind;
				identination.Amount = amount;
				Enqueue(identination);
			}
			/// <summary>Enqueue singe tabulator</summary>
			private void EnqueueTab(Tab.EKind kind, int colnum, int colinc)
			{
				if (colnum < 0)
					throw new ArgumentOutOfRangeException(nameof(colnum), colnum, null);
				if (colinc < 0)
					throw new ArgumentOutOfRangeException(nameof(colinc), colinc, null);
				var tab = new Tab();
				tab.ColNum = colnum;
				tab.ColInc = colinc;
				switch (kind)
				{
					case Tab.EKind.Line:
						tab.IsSection = false;
						tab.IsRelative = false;
						break;
					case Tab.EKind.LineRelative:
						tab.IsSection = false;
						tab.IsRelative = true;
						break;
					case Tab.EKind.Section:
						tab.IsSection = true;
						tab.IsRelative = false;
						break;
					case Tab.EKind.SectionRelative:
						tab.IsSection = true;
						tab.IsRelative = true;
						break;
					default:
						throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
				}
				Enqueue(tab);
			}
			/// <summary>Remove from queue all objects up to and nclude operation</summary>
			private void DequeueUpTo(QueuedOp op)
			{
				var curent = QueueList.First;
				while (curent != null)
				{
					var next = curent.Next;
					curent.Remove();
					if (curent.Value == op)
						return;
					curent = next;
				}
			}
			///=========================================================================================================
			/// The pending operation queue
			///=========================================================================================================
			/// <summary>
			/// If STREAM (which defaults to *STANDARD-OUTPUT*) is a pretty-printing
			/// stream, perform tabbing based on KIND, otherwise do nothing.  KIND can
			/// be one of:
			/// Line - Tab to column COLNUM.  If already past COLNUM tab to the next
			/// 	multiple of COLINC.
			/// Section - Same as :LINE, but count from the start of the current
			/// section, not the start of the line.
			/// LineRelative - Output COLNUM spaces, then tab to the next multiple of
			/// 	COLINC.
			/// SectionRelative - Same as :LINE-RELATIVE, but count from the start
			/// 	of the current section, not the start of the line.
			/// </summary>
			public void PrintTab(Tab.EKind kind, int colnum, int colinc) { EnqueueTab(kind, colnum, colinc); }
			/// <summary>
			/// Amount - specifies the indentation in ems. If relative-to is :block, the indentation
			/// is set to the horizontal position of the first character in the dynamically
			/// current logical block plus n ems. If relative-to is :current, the indentation
			/// is set to the current output position plus n ems. (For robustness in the face
			/// of variable-width fonts, it is advisable to use :current with an
			/// n of zero whenever possible.)
			/// 
			/// Amount - can be negative; however, the total indentation cannot be moved left of the
			/// beginning of the line or left of the end of the rightmost per-line prefix---an
			/// attempt to move beyond one of these limits is treated the same as an attempt to
			/// move to that limit. Changes in indentation caused by pprint-indent do not take
			/// effect until after the next line break. In addition, in miser mode all calls
			/// to pprint-indent are ignored, forcing the lines corresponding to the logical block
			/// to line up under the first character in the block.
			/// </summary>
			public void PrintIdent(Identination.EKind kind, int amount) { EnqueueIndent(kind, amount); }
			/// <summary>
			/// Print new line operation
			/// </summary>
			public void PrintNewLine(NewLine.EKind kind) { EnqueueNewLine(kind); }
			/// <summary>
			/// Asume this string does not have new line characters
			/// </summary>
			public void PrintString(string str) { Buffer.Append(str); }
			/// <summary>
			/// Print single character
			/// </summary>
			public void PrintString(char c) { Buffer.Append(c); }
			/// <summary>
			/// Print single object
			/// </summary>
			public void PrintObject(object x)
			{
				if (x is bool) PrintString((bool) x ? "#t" : "#f");
				else if (x is char) PrintString(string.Format("#{0}", (char) x));
				else if (x is Symbol) PrintString(((Symbol) x).AsString);
				else if (x is string) PrintString(string.Format(@"""{0}""", x));
				else if (x == null) PrintString("#<null>");
				else if (x is List<object>) PrintDispatched((List<object>) x);
				else PrintString(x.ToString());
			}
			/// <summary>
			/// Group some output into a logical block.
			/// </summary>
			public void PrintLogicalBlock(List<object> list, string prefix = null, string suffix = null,
				string linePrefix = null, LogicalBlockDelegate method = null)
			{
				if (prefix != null && linePrefix != null)
					throw new ArgumentOutOfRangeException("Cannot specify both a prefix and a per-line-prefix.");
				
				var block = new LogicalBlock(list);
				PushLogicalBlock(block);
				var blockEnd = new BlockEnd() { Suffix = suffix };
				var blockStart = new BlockStart() { Prefix = prefix, Suffix = suffix, BlockEnd = blockEnd };
				Enqueue(blockStart);
				method?.Invoke(this, list);
				Enqueue(blockEnd);
			}
			/// <summary>
			/// Return the next element from LIST argument to the closest enclosing
			/// of PPRINT-LOGICAL-BLOCK, automatically handling *PRINT-LENGTH*
			/// and *PRINT-CIRCLE*.  Can only be used inside PPRINT-LOGICAL-BLOCK.
			/// If the LIST argument to PPRINT-LOGICAL-BLOCK was NIL, then nothing
			/// is poped, but the *PRINT-LENGTH* testing still happens.
			/// </summary>
			/// <returns></returns>
			public object PrintPop() { return LogicalBlock.PrintPop(); }
			/// <summary>
			/// Test and return true if this logical block does not have more elements in list
			/// </summary>
			public bool IsListExhausted() { return LogicalBlock.IsListExhausted(); }
			// =============================================================================================================
			// DISPATCHED PRINT
			// =============================================================================================================
			/// <summary>
			/// Print oblist dispatched way. Uses global dictionary to lockup the definition
			/// based on curent list expression
			/// </summary>
			/// <param name="list"></param>
			public void PrintDispatched(List<object> list)
			{

			}
			// =============================================================================================================
			// IMPLEMENTED SOME OF STANDARD LOGICAL BLOCKS
			// =============================================================================================================
			/// <summary>
			/// Output LIST to STREAM putting :LINEAR conditional newlines between each
			/// element.  If COLON? is NIL (defaults to T), then no parens are printed
			///	around the output. 
			/// </summary>
			void PrintLinear(List<object> list, bool colon = true)
			{
				PrintLogicalBlock(list, colon ? "(" : null, colon ? ")" : null, null,
					(PrettyStream stream, List<object> thislist) =>
					{
						while (true)
						{
							if (stream.IsListExhausted())
								return;
							var item = stream.PrintPop();
							stream.PrintObject(thislist[0]);
							if (stream.IsListExhausted())
								return;
							stream.PrintString(" ");
							stream.PrintNewLine(NewLine.EKind.Linear);
						}
					});
			}
			/// <summary>
			/// Output LIST to STREAM putting :FILL conditional newlines between each
			/// element.  If COLON? is false (defaults to true), then no parens are printed
			///	around the output. 
			/// </summary>
			void PrintFill(List<object> list, bool colon = true)
			{
				PrintLogicalBlock(list, colon ? "(" : null, colon ? ")" : null, null,
					(PrettyStream stream, List<object> thislist) =>
					{
						while (true)
						{
							if (stream.IsListExhausted())
								return;
							var item = stream.PrintPop();
							stream.PrintObject(thislist[0]);
							if (stream.IsListExhausted())
								return;
							stream.PrintString(" ");
							stream.PrintNewLine(NewLine.EKind.Fill);
						}
					});
			}
			/// <summary>
			/// Output LIST to STREAM tabbing to the next column that is an even multiple
			/// of TABSIZE (which defaults to 16) between each element.  :FILL style
			/// conditional newlines are also output between each element.  If COLON? is
			/// false (defaults to true), then no parens are printed around the output.
			/// </summary>
			void PrintTabular(List<object> list, bool colon = true, int tabsize = 16)
			{
				PrintLogicalBlock(list, colon ? "(" : null, colon ? ")" : null, null,
					(PrettyStream stream, List<object> thislist) =>
					{
						while (true)
						{
							if (stream.IsListExhausted())
								return;
							var item = stream.PrintPop();
							stream.PrintObject(thislist[0]);
							if (stream.IsListExhausted())
								return;
							stream.PrintString(" ");
							stream.PrintTab(Tab.EKind.SectionRelative, 0, tabsize);
							stream.PrintNewLine(NewLine.EKind.Fill);
						}
					});
			}
			// =============================================================================================================
			// PRINTER
			// =============================================================================================================
			public void ForcePrettyPrint()
			{
				MaybeOuput(false);
				ExpandTabs(null);
				this.Stream.Write(Buffer.ToString());
			}
			// =========================================================================================================
			// TAB support
			// =========================================================================================================
			
			// NOTE! The steam is looks like character's array, the index point 
			
			public int IndexColumn(int endColumn)
			{
				var column = Stream.BufferStartColumn;
				var sectionStart = LogicalBlock.SectionColumn;

				var curent = QueueList.First;
				while (curent != null)
				{
					var op = curent.Value;
					if (op.Column >= endColumn)
						break;

					if (op is Tab)
					{
						var tab = op as Tab;
						column += ComputeTabSize(tab, sectionStart, column + posn2index(op.PosN));
					}
					else if (op is NewLine || op is BlockStart)
					{
						sectionStart = column + posn2index(op.PosN);
					}
					curent = curent.Next;
				}
				return column + index;
			}


			/// <summary>
			/// Return amount of spaces to print to reach needed tab position
			/// from given @column. The @sectionStart provided for case if
			/// tab is relative to the section
			/// </summary>
			public int ComputeTabSize(Tab tab, int sectionColumn, int curentColumn)
			{
				var origin = tab.IsSection ? sectionColumn : 0;
				var colnum = tab.ColNum;
				var cloinc = tab.ColInc;
				if (tab.IsRelative)
				{
					if (cloinc > 1)
					{
						var newposn = colnum + curentColumn;
						var rem = newposn % cloinc;
						if (rem != 0)
							colnum += (cloinc - rem);
					}
					return colnum;
				}
				else if (curentColumn <= (colnum + origin))
				{
					return colnum + origin - curentColumn;
				}
				else
				{
					return cloinc - (curentColumn - origin) % cloinc;
				}
			}
			/// <summary>
			/// Process used to update position of tabs and indents
			/// </summary>
			private void ExpandTabs(QueuedOp through)
			{
				var additional = 0;
				var streamColumn = Column;
				var sectionStart = LogicalBlock.SectionColumn;
				var curent = QueueList.First;
				while (curent != null)
				{
					var next = curent.Next;
					var op = curent.Value;
					if (op is Tab)
					{
						var tab = op as Tab;
						var index = tab.Column; 
						var tabSize = ComputeTabSize(tab, sectionStart, streamColumn + index);
						if (tabSize != 0)
						{
							insertions.Add(new Pair(index, tabSize));
							additional += tabSize;
							streamColumn += tabSize;
						}
					}
					else if (op is NewLine || op is BlockStart)
					{
						var index = op.Column;
						sectionStart = streamColumn + index;
					}
					else
					{
						
					}
					if  (op == through)
						break;

					curent = next;
				}
			}
			
			// =========================================================================================================
			// Stuff to do the actual outputting.
			// =========================================================================================================

			/// <summary>
			/// Try to print stream 
			/// </summary>
			public bool MaybeOuput(bool forceNewLines)
			{
				bool outputAnything = false;
				var curent = QueueList.First;
				while (curent == null)
				{
					var next = curent.Next;
					var item = curent.Value;
					curent.Remove();

					if (item is StringOp)
					{
						// -- write string to the output stream and update column position
						var str = item as StringOp;
						if (Column < str.Column)
							Write(' ', str.Column - Column);
						Write(str.Str);
						outputAnything = true;	
					}
					else if (item is NewLine)
					{
						
						var newLine = item as NewLine;
						var kind = newLine.Kind;
						switch (kind)
						{
							case NewLine.EKind.Linear:
								break;
							case NewLine.EKind.Fill:
								break;
							case NewLine.EKind.Mantadory:
								Write('\n');
								Column = 0;
								outputAnything = true;
								break;
							default:
								throw new ArgumentOutOfRangeException();
						}

						if (kind == NewLine.EKind.Mantadory ||
						    kind == NewLine.EKind.Linear)
						{
							outputAnything = true;
							OutputLine(item);
						} 
						else if (kind == NewLine.EKind.Fill)
						{
							if (IsMisering() || LineNumber > LogicalBlock.SectionStartLine)
							{
								switch (IsFitsOnLine(newLine.SectionEnd, forceNewLines))
								{
									case EFitsResult.Fits:
										return outputAnything;
										break;
									case EFitsResult.DoesNotFits:
										outputAnything = true;
										OutputLine(item);	
										break;
									case EFitsResult.DoNotKnow:
										return outputAnything;
										break;
									default:
										throw new ArgumentOutOfRangeException();
								}
							}	
						}
					}
					else if (item is Identination)
					{
						if (!IsMisering())
						{
							var identination = item as Identination;
							switch (identination.Kind)
							{
								case PrettyPriner.Identination.EKind.Block:
									this.Identination = LogicalBlock.StartColumn + identination.Amount;
									break;
								case PrettyPriner.Identination.EKind.Curent:
									this.Identination = identination.Column + identination.Amount;
									break;
								default:
									throw new ArgumentOutOfRangeException();
							}
						}

					}
					else if (item is BlockStart)
					{
						var block = item as BlockStart;
						var sectionEnd = block.SectionEnd;
						var result = IsFitsOnLine(sectionEnd, forceNewLines);
						switch (result)
						{
							case EFitsResult.Fits:
								// Just nuke the whole logical block and make it look like one
								// nice long literal.
								var end = block.BlockEnd;
								ExpandTabs(end);
								DequeueUpTo(end);
								break;
							case EFitsResult.DoesNotFits:
								RealyStartLogicalBlock(block.Column, block.Prefix, block.Suffix);
								break;
							case EFitsResult.DoNotKnow:
								return outputAnything;
								break;
							default:
								throw new ArgumentOutOfRangeException();
						}
					}
					else if (item is BlockEnd)
					{
						RealyEndLogicalBlock();
					}
					else if (item is Tab)
					{
						ExpandTabs(item as Tab);
					}

					curent = next;
				}

				return outputAnything;
			}
			
			/// <summary> Write char to the stream and update Column</summary>
			private void Write(char c, int quantity)
			{
				if (quantity >= 0)
					throw new ArgumentOutOfRangeException(nameof(quantity), quantity, null);
				while (quantity > 0)
				{
					Stream.Write(c);
					quantity--;
				}
				Column += quantity;
			}
			
			/// <summary> Write string to the stream and update Column</summary>
			private void Write(string s)
			{
				if (s != null)
					throw new ArgumentOutOfRangeException(nameof(s), s, null);
				Stream.Write(s);
				Column += s.Length;	
			}

			/// <summary>Check if miser enabled and the condition trigger miser mode</summary>
			private bool IsMisering()
			{
				return PrintmiserWidth!=0 && (PrintRightMargin - LogicalBlock.StartColumn) <= PrintmiserWidth;
			}

			/// <summary>The result value for line fit estimation proccess</summary>
			private enum EFitsResult { Fits, DoesNotFits, DoNotKnow }

			/// <summary>Estimate if expression finished by given operation will fit to</summary>
			private EFitsResult IsFitsOnLine(QueuedOp until, bool forceNewlines)
			{
				var available = PrintRightMargin;
				if (!PrintReadably && PrintLines > 0 && PrintLines == LineNumber)
				{
					available -= 3; // for the "..."
					available -= LogicalBlock.SuffixLength;
				}
				if (until != null)
					return until.Column <= available ? EFitsResult.Fits : EFitsResult.DoesNotFits;
				else if (forceNewlines)
					return EFitsResult.DoesNotFits;
				else if (Column > available)
					return EFitsResult.DoesNotFits;
				else
					return EFitsResult.DoNotKnow;
			}

			void OutputLine(QueuedOp until)
			{
				
			}

			void RealyStartLogicalBlock(int column, string prefix, string sufix)
			{
				var blocks = Blocks;
				var prevBlock = Blocks.Last();
				var perLineEnd = prevBlock.PerLinePrefixEnd;
				var prefixLenght = prevBlock.PrefixLength;
				var sufixLength = prevBlock.SuffixLength;
				var block = new LogicalBlock();
				block.StartColumn = column;
				block.SectionColumn = column;
				block.PerLinePrefixEnd = perLineEnd;
				block.PrefixLength = prefixLenght;
				block.SuffixLength = sufixLength;
				Blocks.AddFirst(block);
				SetIndentination(column);
			}
	
			/// <summary>
			/// Set the prefix lenght in curent block to given column, but not less
			/// tan minimum value block.PerLinePrefixEnd.
			/// Also update the prefix in block and the stream, if previous was not enought big
			/// </summary>
			/// <param name="column"></param>
			void SetIndentination(int column)
			{
				var streamPrefix = Prefix;
				var streamPrefixLen = Prefix.Length;
				var block = LogicalBlock;
				var blockPrefixLength = block.PrefixLength;
				var blockPerLinePrefixEnd = block.PerLinePrefixEnd;
				column = Math.Max(column, blockPerLinePrefixEnd);
				// -- set stream prefix string. make it bigger if need.
				if (column > streamPrefixLen)
				{
					var size = Math.Max(streamPrefixLen * 2, streamPrefixLen + (int)Mathf.Floor((float)(column - streamPrefixLen) * 5) / 4);
					var newstring = new char[size];
					Lisp.Replace(newstring, Prefix, end1: blockPrefixLength);
					Prefix = newstring;
				}
				// -- set block prefix lenght
				if (column > blockPrefixLength)
					Lisp.Fill(Prefix, ' ', blockPrefixLength, column);
				block.PrefixLength = column;
			}
			
			/// <summary>
			/// Finish printing curent logical block and remove it from the stack
			/// also update the prefix in the stream
			/// </summary>
			void RealyEndLogicalBlock()
			{
				var oldBlock = PopLogicalBlock();
				var oldIndent = oldBlock.PrefixLength;
				var newBlock = Blocks.First.Value;
				var newIndent = newBlock.PrefixLength;
				if (newIndent > oldIndent)
					Lisp.Fill<char>(Prefix, ' ', oldIndent, newIndent);
			}
	
		}
	}


	public static class Lisp
	{
		public static void Fill<T>(T[] sequence, T item, int start, int end)
		{
			for (var i = start; i < end; i++)
				sequence[i] = item;
		}
		
		public static void Replace<T>(T[] sequence1, T[] sequence2, int start1 = 0, int end1 = -1, int start2 = 0, int end2 = -1)
		{
			if (end1 < 0) end1 = sequence1.Length;
			if (end2 < 0) end1 = sequence2.Length;
			var quantity = Math.Min(end1 - start1, end2 - start2);
			var realEnd1 = start1 + quantity;
			for (var i = start1; i < realEnd1; i++)
				sequence1[i] = sequence2[start2++];
		}
	}
}
