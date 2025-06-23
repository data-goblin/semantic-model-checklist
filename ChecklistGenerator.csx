#r "System.Windows.Forms"
#r "System.Drawing"
#r "System.Net.Http"
using System;
using System.IO;
using System.Windows.Forms;
using System.Drawing;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using TabularEditor.TOMWrapper;

ScriptHelper.WaitFormVisible = false;

// GitHub repository configuration
public static class GitHubConfig
{
    public const string BaseUrl = "https://raw.githubusercontent.com/data-goblin/semantic-model-checklist/main/";
    public const string ChecklistItemsUrl = BaseUrl + "checklist-items.yaml";
    public const string CssUrl = BaseUrl + "style.css";
    public const string HtmlTemplateUrl = BaseUrl + "template.html";
    public const string IconsUrl = BaseUrl + "icons.json";
}

public class ChecklistSection
{
    public string Title { get; set; }
    public List<ChecklistItem> Items { get; set; } = new List<ChecklistItem>();
    public bool IsSubsection { get; set; } = false;
}

public class ChecklistItem
{
    public string Text { get; set; }
    public string Reference { get; set; }
    public bool IsChecked { get; set; } = false;
    public string AutoCheckReason { get; set; }
}

public class ModelAnalysis
{
    public bool HasDescriptions { get; set; }
    public bool HasDisplayFolders { get; set; }
    public bool HasMeasureTables { get; set; }
    public bool HasHiddenTechnicalFields { get; set; }
    public bool HasRelationships { get; set; }
    public bool HasCleanNaming { get; set; }
    public bool HasDataTypes { get; set; }
    public int TechnicalColumnsCount { get; set; }
    public int MeasuresWithFoldersCount { get; set; }
    public int ColumnsWithDescriptionsCount { get; set; }
    
    public bool HasVertiPaqStats { get; set; }
    public long TotalModelSizeBytes { get; set; }
    public string FormattedModelSize { get; set; }
    public object ModelStats { get; set; }
    public List<(string Name, long RowCount, long Size)> TableStats { get; set; }
    public List<(string TableName, string ColumnName, long Size)> ColumnStats { get; set; }
}

public class GenerationOptions
{
    public string OutputPath { get; set; }
    public bool IncludeReferences { get; set; } = true;
    public bool IncludeInstructions { get; set; } = true;
    public string ProjectName { get; set; } = "My Semantic Model";
    public string ModelTitle { get; set; } = "";
    public bool UseOfflineMode { get; set; } = false;
    
    // New options for user selection
    public bool IncludeModelStats { get; set; } = true;
    public bool IncludeTimeline { get; set; } = true;
    public bool IncludeChecklists { get; set; } = true;
}

async Task Main()
{
    try
    {
        // Step 1: Get user preferences and initialize
        var options = ShowOptionsDialog();
        if (options == null)
        {
            return;
        }

        // Only analyze model if user wants stats
        ModelAnalysis analysis = null;
        if (options.IncludeModelStats)
        {
            var analysisResult = MessageBox.Show(
                "MODEL ANALYSIS REQUIRED\n\n" +
                "You selected to include model summary statistics. The script will now analyze " +
                "your current model and run VertiPaq statistics. This may take a few seconds " +
                "depending on your model size.\n\n" +
                "TIP: You can view detailed VertiPaq statistics in the 'VertiPaq Analyzer' tab " +
                "of Tabular Editor 3 after the checklist is generated.\n\n" +
                "Click OK to begin analysis or Cancel to exclude model statistics.",
                "Semantic Model Checklist - Model Analysis",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information
            );

            if (analysisResult == DialogResult.Cancel)
            {
                options.IncludeModelStats = false;
                analysis = new ModelAnalysis(); // Empty analysis
            }
            else
            {
                analysis = AnalyzeCurrentModel();
            }
        }
        else
        {
            analysis = new ModelAnalysis(); // Empty analysis
        }
        
        // Step 2: Download from GitHub if needed
        List<ChecklistSection> checklistSections = new List<ChecklistSection>();
        if (options.IncludeChecklists)
        {
            var downloadResult = MessageBox.Show(
                "DOWNLOAD LATEST CHECKLISTS\n\n" +
                "The script will now retrieve the latest checklist items and styles from:\n" +
                "https://github.com/data-goblin/semantic-model-checklist\n\n" +
                "This ensures you have the most up-to-date checklist with all recent improvements. " +
                "An internet connection is required for this step.\n\n" +
                "Click OK to download the latest checklist or Cancel to use built-in checklists.",
                "Semantic Model Checklist - Download",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information
            );

            if (downloadResult == DialogResult.Cancel)
            {
                // User cancelled - no checklists will be included
                checklistSections = new List<ChecklistSection>();
            }
            else
            {
                checklistSections = await GetChecklistDataFromGitHub(analysis, options.UseOfflineMode);
                if (!checklistSections.Any())
                {
                    // GitHub download failed - no checklists available
                    MessageBox.Show(
                        "DOWNLOAD FAILED\n\n" +
                        "Could not download checklists from GitHub. No checklist content will be included.\n\n" +
                        "Only model statistics and timeline (if selected) will be generated.",
                        "Semantic Model Checklist - No Content",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    checklistSections = new List<ChecklistSection>();
                }
            }
            
            ApplyAutoAnalysis(checklistSections, analysis);
        }
        
        await GenerateEnhancedHtmlChecklist(checklistSections, options, analysis);
    }
    catch (Exception ex)
    {
        MessageBox.Show(
            "ERROR GENERATING CHECKLIST\n\n" +
            $"An error occurred: {ex.Message}\n\n" +
            "Please try again or check your internet connection if the error relates to downloading content.",
            "Semantic Model Checklist - Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error
        );
    }
}

GenerationOptions ShowOptionsDialog()
{
    var form = new Form
    {
        Text = "Semantic Model Checklist Options",
        Size = new Size(500, 400),
        StartPosition = FormStartPosition.CenterScreen,
        FormBorderStyle = FormBorderStyle.FixedDialog,
        MaximizeBox = false,
        MinimizeBox = false
    };

    var titleLabel = new Label
    {
        Text = "TABULAR EDITOR 3 SEMANTIC MODEL CHECKLIST",
        Font = new Font("Segoe UI", 12, FontStyle.Bold),
        Size = new Size(460, 25),
        Location = new Point(20, 20),
        TextAlign = ContentAlignment.MiddleCenter
    };

    var descLabel = new Label
    {
        Text = "Select what you want to include in your checklist:",
        Size = new Size(460, 20),
        Location = new Point(20, 55)
    };

    var statsCheckBox = new CheckBox
    {
        Text = "Model summary statistics (tables, columns, measures, VertiPaq analysis)",
        Size = new Size(450, 20),
        Location = new Point(30, 85),
        Checked = true
    };

    var timelineCheckBox = new CheckBox
    {
        Text = "Steps to build a model (interactive timeline)",
        Size = new Size(450, 20),
        Location = new Point(30, 115),
        Checked = true
    };

    var checklistsCheckBox = new CheckBox
    {
        Text = "Checklists (Design, Build, Data Sources, Documentation, Training, etc.)",
        Size = new Size(450, 20),
        Location = new Point(30, 145),
        Checked = true
    };

    var noteLabel = new Label
    {
        Text = "NOTE: At least one option must be selected to generate a useful checklist.",
        Font = new Font("Segoe UI", 8, FontStyle.Italic),
        ForeColor = Color.Gray,
        Size = new Size(460, 20),
        Location = new Point(20, 180)
    };

    var projectLabel = new Label
    {
        Text = "Project Name:",
        Size = new Size(100, 20),
        Location = new Point(20, 220)
    };

    var projectTextBox = new TextBox
    {
        Text = "Power BI Project",
        Size = new Size(200, 20),
        Location = new Point(130, 218)
    };

    var modelLabel = new Label
    {
        Text = "Model Title:",
        Size = new Size(100, 20),
        Location = new Point(20, 250)
    };

    var modelTextBox = new TextBox
    {
        Text = Model?.Name ?? "Semantic Model",
        Size = new Size(200, 20),
        Location = new Point(130, 248)
    };

    var okButton = new Button
    {
        Text = "Generate Checklist",
        Size = new Size(120, 30),
        Location = new Point(250, 290),
        DialogResult = DialogResult.OK
    };

    var cancelButton = new Button
    {
        Text = "Cancel",
        Size = new Size(80, 30),
        Location = new Point(380, 290),
        DialogResult = DialogResult.Cancel
    };

    // Validation logic
    okButton.Click += (s, e) =>
    {
        if (!statsCheckBox.Checked && !timelineCheckBox.Checked && !checklistsCheckBox.Checked)
        {
            MessageBox.Show(
                "SELECTION REQUIRED\n\n" +
                "Please select at least one option to generate a useful checklist.",
                "Semantic Model Checklist - Validation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
            form.DialogResult = DialogResult.None;
        }
    };

    form.Controls.AddRange(new Control[] {
        titleLabel, descLabel, statsCheckBox, timelineCheckBox, checklistsCheckBox,
        noteLabel, projectLabel, projectTextBox, modelLabel, modelTextBox, okButton, cancelButton
    });

    form.AcceptButton = okButton;
    form.CancelButton = cancelButton;

    var result = form.ShowDialog();
    
    if (result == DialogResult.OK)
    {
        return new GenerationOptions
        {
            ProjectName = projectTextBox.Text,
            ModelTitle = modelTextBox.Text,
            OutputPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                                     (modelTextBox.Text ?? "SemanticModel").Replace(" ", "_") + "_Checklist"),
            IncludeReferences = true,
            IncludeInstructions = true,
            UseOfflineMode = false,
            IncludeModelStats = statsCheckBox.Checked,
            IncludeTimeline = timelineCheckBox.Checked,
            IncludeChecklists = checklistsCheckBox.Checked
        };
    }
    
    return null;
}

async Task<List<ChecklistSection>> GetChecklistDataFromGitHub(ModelAnalysis analysis, bool useOfflineMode = false)
{
    if (useOfflineMode)
    {
        MessageBox.Show(
            "OFFLINE MODE SELECTED\n\n" +
            "Checklist generation failed because offline mode was selected. " +
            "Please enable online mode to download the latest checklist.",
            "Semantic Model Checklist - Offline Mode",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning
        );
        return new List<ChecklistSection>();
    }
    
    try
    {
        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            var yamlString = await client.GetStringAsync(GitHubConfig.ChecklistItemsUrl);
            var sections = ParseYamlChecklist(yamlString);
            
            ApplyAutoAnalysis(sections, analysis);
            return sections;
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show(
            "DOWNLOAD FAILED\n\n" +
            $"Failed to download checklist from GitHub: {ex.Message}\n\n" +
            "Please check your internet connection and try again.",
            "Semantic Model Checklist - Download Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error
        );
        return new List<ChecklistSection>();
    }
}

List<ChecklistSection> ParseYamlChecklist(string yamlContent)
{
    var sections = new List<ChecklistSection>();
    var lines = yamlContent.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
    
    ChecklistSection currentSection = null;
    ChecklistItem currentItem = null;
    
    for (int i = 0; i < lines.Length; i++)
    {
        var line = lines[i];
        var trimmed = line.Trim();
        
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
            continue;
            
        if (line.StartsWith("- title:"))
        {
            if (currentSection != null)
                sections.Add(currentSection);
                
            currentSection = new ChecklistSection
            {
                Title = ExtractYamlValue(line, "title:")
            };
        }
        else if (line.StartsWith("  isSubsection:"))
        {
            if (currentSection != null)
                currentSection.IsSubsection = ExtractYamlValue(line, "isSubsection:").ToLower() == "true";
        }
        else if (line.StartsWith("  items:"))
        {
            // Items array starts
        }
        else if (line.StartsWith("    - text:"))
        {
            currentItem = new ChecklistItem
            {
                Text = ExtractYamlValue(line, "text:")
            };
            if (currentSection != null)
                currentSection.Items.Add(currentItem);
        }
        else if (line.StartsWith("      reference:") && currentItem != null)
        {
            currentItem.Reference = ExtractYamlValue(line, "reference:");
        }
    }
    
    if (currentSection != null)
        sections.Add(currentSection);
    
    return sections;
}

string ExtractYamlValue(string line, string key)
{
    var keyIndex = line.IndexOf(key);
    if (keyIndex == -1) return "";
    
    var valueStart = keyIndex + key.Length;
    var value = line.Substring(valueStart).Trim();
    
    if ((value.StartsWith("\"") && value.EndsWith("\"")) || 
        (value.StartsWith("'") && value.EndsWith("'")))
    {
        value = value.Substring(1, value.Length - 2);
    }
    
    return value;
}

List<ChecklistSection> GetBuiltInChecklistSections()
{
    return new List<ChecklistSection>();
}

void ApplyAutoAnalysis(List<ChecklistSection> sections, ModelAnalysis analysis)
{
    foreach (var section in sections)
    {
        foreach (var item in section.Items)
        {
            var itemText = item.Text.ToLower();
            
            if (itemText.Contains("define main semantic model tables") && Model?.Tables?.Count > 0)
            {
                item.IsChecked = true;
                item.AutoCheckReason = $"Found {Model.Tables.Count} tables in model";
            }
            else if (itemText.Contains("organize expressions") && analysis.HasDisplayFolders)
            {
                item.IsChecked = true;
                item.AutoCheckReason = $"Display folders found on {analysis.MeasuresWithFoldersCount} measures";
            }
            else if (itemText.Contains("hide technical") && analysis.HasHiddenTechnicalFields)
            {
                item.IsChecked = true;
                item.AutoCheckReason = $"Technical fields properly hidden ({analysis.TechnicalColumnsCount} found)";
            }
            else if (itemText.Contains("descriptions") && analysis.HasDescriptions)
            {
                item.IsChecked = true;
                item.AutoCheckReason = $"Descriptions found on {analysis.ColumnsWithDescriptionsCount} fields";
            }
            else if (itemText.Contains("measure tables") && analysis.HasMeasureTables)
            {
                item.IsChecked = true;
                item.AutoCheckReason = "Dedicated measure tables detected";
            }
            else if (itemText.Contains("relationships") && analysis.HasRelationships)
            {
                item.IsChecked = true;
                item.AutoCheckReason = $"Found {Model?.Relationships?.Count ?? 0} relationships";
            }
            else if (itemText.Contains("datatypes") && analysis.HasDataTypes)
            {
                item.IsChecked = true;
                item.AutoCheckReason = "Appropriate data types detected";
            }
            else if (itemText.Contains("tabular editor") && itemText.Contains("management tools"))
            {
                item.IsChecked = true;
                item.AutoCheckReason = "Using Tabular Editor 3";
            }
            else if (itemText.Contains("model object names") && analysis.HasCleanNaming)
            {
                item.IsChecked = true;
                item.AutoCheckReason = "Clean naming patterns detected";
            }
            else if (itemText.Contains("basic info") && !string.IsNullOrEmpty(Model?.Name))
            {
                item.IsChecked = true;
                item.AutoCheckReason = $"Model name: {Model.Name}";
            }
        }
    }
}

async Task GenerateEnhancedHtmlChecklist(List<ChecklistSection> sections, GenerationOptions options, ModelAnalysis analysis)
{
    string safeOutputPath = EnsureSafeOutputPath(options.OutputPath);
    string htmlPath = safeOutputPath + ".html";
    
    try
    {
        string htmlTemplate, iconsJson, cssContent;
        
        // Get checklist and styles from GitHub
        using (var client = new HttpClient())
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            htmlTemplate = await client.GetStringAsync(GitHubConfig.HtmlTemplateUrl);
            iconsJson = await client.GetStringAsync(GitHubConfig.IconsUrl);
            cssContent = await client.GetStringAsync(GitHubConfig.CssUrl);
        }
        
        // Extract icons from JSON
        var te3Logo = ExtractIconFromJson(iconsJson, "te3_logo");
        var modelSizeIcon = ExtractIconFromJson(iconsJson, "model_size");
        var tablesIcon = ExtractIconFromJson(iconsJson, "tables");
        var columnsIcon = ExtractIconFromJson(iconsJson, "columns");
        var measuresIcon = ExtractIconFromJson(iconsJson, "measures");
        
        // Replace template placeholders
        var html = htmlTemplate
            .Replace("{{PROJECT_NAME}}", options.ProjectName)
            .Replace("{{CSS_URL}}", "") // Remove external CSS link
            .Replace("{{TE3_LOGO}}", te3Logo)
            .Replace("{{GENERATION_DATE}}", DateTime.Now.ToString("MMMM dd, yyyy"));
        
        html = html.Replace("</head>", $"<style>{cssContent}</style></head>");
        
        if (!options.IncludeTimeline)
        {
            var timelineStart = html.IndexOf("<div class='timeline-section'>");
            if (timelineStart >= 0)
            {
                var searchPos = timelineStart;
                var divCount = 0;
                var timelineEnd = -1;
                
                while (searchPos < html.Length)
                {
                    var nextOpenDiv = html.IndexOf("<div", searchPos);
                    var nextCloseDiv = html.IndexOf("</div>", searchPos);
                    
                    if (nextOpenDiv >= 0 && (nextCloseDiv < 0 || nextOpenDiv < nextCloseDiv))
                    {
                        divCount++;
                        searchPos = nextOpenDiv + 4;
                    }
                    else if (nextCloseDiv >= 0)
                    {
                        divCount--;
                        searchPos = nextCloseDiv + 6;
                        if (divCount == 0)
                        {
                            timelineEnd = searchPos;
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                
                if (timelineEnd > timelineStart)
                {
                    html = html.Remove(timelineStart, timelineEnd - timelineStart);
                }
            }
        }
        
        // Generate model info section
        var modelInfoSection = options.IncludeModelStats ? 
            GenerateModelInfoSection(analysis, options, modelSizeIcon, tablesIcon, columnsIcon, measuresIcon) : "";
        html = html.Replace("{{MODEL_INFO_SECTION}}", modelInfoSection);
        
        // Generate instructions section
        var instructionsSection = "";
        if (options.IncludeInstructions && (options.IncludeChecklists || options.IncludeTimeline))
        {
            instructionsSection = @"
        <div class='instructions'>
            <h3>How to Use This Enhanced Checklist</h3>
            <ul>";
            
            if (options.IncludeChecklists)
            {
                instructionsSection += @"
                <li><strong>Print as PDF:</strong> Use Ctrl+P and select 'Save as PDF' to create a form-fillable document</li>
                <li><strong>Auto-checked items:</strong> Items highlighted in green have been automatically checked based on your model analysis</li>
                <li><strong>Reference links:</strong> Click the colored '?' buttons for additional documentation</li>
                <li><strong>Notes sections:</strong> Use the text areas at the end of each section for custom notes</li>";
            }
            
            if (options.IncludeTimeline)
            {
                instructionsSection += @"
                <li><strong>Timeline:</strong> Track your progress through the semantic model development journey</li>";
            }
            
            instructionsSection += @"
            </ul>
        </div>";
        }
        
        html = html.Replace("{{INSTRUCTIONS_SECTION}}", instructionsSection);
        
        // Handle timeline section
        if (!options.IncludeTimeline)
        {
            // Remove timeline section from template
            var timelineStart = html.IndexOf("<div class='timeline-section'>");
            if (timelineStart >= 0)
            {
                var timelineEnd = html.IndexOf("</div>", html.IndexOf("</div>", html.IndexOf("</div>", timelineStart) + 6) + 6) + 6;
                if (timelineEnd >= 0)
                {
                    html = html.Remove(timelineStart, timelineEnd - timelineStart);
                }
            }
        }
        
        // Generate checklist sections
        var checklistHtml = new StringBuilder();
        if (options.IncludeChecklists && sections.Any())
        {
            int sectionIndex = 0;
            foreach (var section in sections.Where(s => s.Items.Any()))
            {
                var backgroundColors = new[] { "#8fcea4", "#ecb171", "#7fd3ce" };
                var bgColor = backgroundColors[sectionIndex % 3];
                
                checklistHtml.AppendLine($@"
                <div class='section'>
                    <div class='section-header'>
                        <h2 class='section-title'>{section.Title}</h2>
                    </div>
                    <div class='section-content' style='background-color: {bgColor}33 !important; padding: 20px;'>");
                
                foreach (var item in section.Items)
                {
                    var itemId = Guid.NewGuid().ToString("N")[..8];
                    string checkedAttr = item.IsChecked ? "checked" : "";
                    string autoCheckedClass = item.IsChecked ? "auto-checked" : "";
                    
                    checklistHtml.AppendLine($@"
                    <div class='checklist-item {autoCheckedClass}' id='item-container-{itemId}'>"); 
                    
                    if (options.IncludeReferences && !string.IsNullOrEmpty(item.Reference))
                    {
                        checklistHtml.AppendLine($@"                        <a href='{item.Reference}' class='reference-link' target='_blank' title='View reference documentation'>?</a>");
                    }
                    else if (options.IncludeReferences)
                    {
                        checklistHtml.AppendLine($@"                        <span class='reference-placeholder'></span>");
                    }
                    
                    checklistHtml.AppendLine($@"                        <input type='checkbox' class='checkbox' name='item_{itemId}' id='checkbox-{itemId}' {checkedAttr}>
                        <div class='item-text'>
                            {item.Text}");
                    
                    if (item.IsChecked && !string.IsNullOrEmpty(item.AutoCheckReason))
                    {
                        checklistHtml.AppendLine($@"                            <div class='auto-check-reason'>
                                <span>Auto-checked: {item.AutoCheckReason}</span>
                                <button class='undo-auto-check' onclick='undoAutoCheck(""{itemId}"")'>undo</button>
                            </div>");
                    }
                    
                    checklistHtml.AppendLine($@"                        </div>
                    </div>");
                }
                
                checklistHtml.AppendLine($@"
                    <div class='notes-section'>
                        <div class='notes-label'>Notes & Comments:</div>
                        <textarea class='notes-area' placeholder='Add your notes, observations, or action items for this section...'></textarea>
                    </div>
                </div>
                </div>");
                
                sectionIndex++;
            }
        }
        
        html = html.Replace("{{CHECKLIST_SECTIONS}}", checklistHtml.ToString());
        
        // Add footer stats
        var autoCheckedCount = sections.SelectMany(s => s.Items).Count(i => i.IsChecked);
        var totalItems = sections.SelectMany(s => s.Items).Count();
        html = html.Replace("{{AUTO_CHECKED_COUNT}}", autoCheckedCount.ToString());
        html = html.Replace("{{TOTAL_ITEMS}}", totalItems.ToString());
        
        // Add JavaScript - only if needed for checklists
        if (options.IncludeChecklists)
        {
            var additionalJavaScript = @"
        function undoAutoCheck(itemId) {
            var container = document.getElementById('item-container-' + itemId);
            var checkbox = document.getElementById('checkbox-' + itemId);
            var autoCheckReason = container.querySelector('.auto-check-reason');
            
            if (container && checkbox && autoCheckReason) {
                container.classList.remove('auto-checked');
                checkbox.checked = false;
                autoCheckReason.style.display = 'none';
            }
        }
        ";
            
            // Insert JavaScript
            if (html.Contains("document.addEventListener('DOMContentLoaded', updateTimelineProgress);"))
            {
                html = html.Replace("document.addEventListener('DOMContentLoaded', updateTimelineProgress);", 
                                   "document.addEventListener('DOMContentLoaded', updateTimelineProgress);\n\n" + additionalJavaScript);
            }
            else
            {
                // Add basic script section for checklist-only
                html = html.Replace("</body>", $"<script>{additionalJavaScript}</script></body>");
            }
        }
        
        File.WriteAllText(htmlPath, html);
        
        // Final success message
        var includedItems = new List<string>();
        if (options.IncludeModelStats) includedItems.Add("Model Statistics");
        if (options.IncludeTimeline) includedItems.Add("Development Timeline");
        if (options.IncludeChecklists) includedItems.Add($"Checklists ({sections.Count} sections)");
        
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = htmlPath,
                UseShellExecute = true
            });
            
            MessageBox.Show(
                "CHECKLIST GENERATED SUCCESSFULLY\n\n" +
                $"Model: {options.ModelTitle}\n" +
                $"Included: {string.Join(", ", includedItems)}\n" +
                (options.IncludeChecklists ? $"Auto-checked items: {autoCheckedCount} of {totalItems}\n" : "") +
                $"Location: {htmlPath}\n\n" +
                "USAGE TIPS:\n" +
                "• Use Ctrl+P to save as PDF for a printable version\n" +
                (options.IncludeChecklists ? "• Click reference links (?) for additional documentation\n" : "") +
                (options.IncludeTimeline ? "• Use the timeline to track your development progress\n" : "") +
                "\nCONTRIBUTE:\n" +
                "Have suggestions for improvements? Fork the repository or submit a PR at:\n" +
                "https://github.com/data-goblin/semantic-model-checklist",
                "Semantic Model Checklist - Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
        catch
        {
            MessageBox.Show(
                "CHECKLIST GENERATED SUCCESSFULLY\n\n" +
                $"Your checklist has been created at:\n{htmlPath}\n\n" +
                $"Included: {string.Join(", ", includedItems)}\n\n" +
                "Have suggestions for improvements? Fork the repository or submit a PR at:\n" +
                "https://github.com/data-goblin/semantic-model-checklist",
                "Semantic Model Checklist - Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show(
            "GENERATION FAILED\n\n" +
            $"Failed to generate checklist: {ex.Message}\n\n" +
            "Please check your internet connection and try again.",
            "Semantic Model Checklist - Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error
        );
    }
}

string ExtractIconFromJson(string iconsJson, string iconKey)
{
    try
    {
        var startPattern = $"\"{iconKey}\": \"";
        var startIndex = iconsJson.IndexOf(startPattern);
        if (startIndex == -1) return "";
        
        startIndex += startPattern.Length;
        var endIndex = iconsJson.IndexOf("\"", startIndex);
        if (endIndex == -1) return "";
        
        return iconsJson.Substring(startIndex, endIndex - startIndex);
    }
    catch
    {
        return "";
    }
}

string GenerateMinimalHtmlTemplate()
{
    return @"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>{{PROJECT_NAME}} - Semantic Model Report</title>
</head>
<body>
    <div class='container'>
        <div class='header'>
            {{TE3_LOGO}}
            <h1>{{PROJECT_NAME}}</h1>
            <h2>SEMANTIC MODEL SUMMARY</h2>
            <p><strong>Generated: {{GENERATION_DATE}}</strong></p>
        </div>

        {{MODEL_INFO_SECTION}}
        {{INSTRUCTIONS_SECTION}}
        {{CHECKLIST_SECTIONS}}

        <div class='footer'>
            <p>Generated by Tabular Editor 3 • <a href='https://github.com/data-goblin/semantic-model-checklist'>View on GitHub</a></p>
        </div>
    </div>
</body>
</html>";
}


string GenerateModelInfoSection(ModelAnalysis analysis, GenerationOptions options, string modelIcon, string tablesIcon, string columnsIcon, string measuresIcon)
{
    if (!analysis.HasVertiPaqStats || analysis.ModelStats == null)
    {
        return $@"
        <div class='model-info'>
            <h3>Model: {options.ModelTitle}</h3>
            <p>This checklist has been automatically analyzed based on your current semantic model structure and contains pre-checked items where applicable.</p>
        </div>";
    }
    
    var modelStats = (dynamic)analysis.ModelStats;
    var html = new StringBuilder();
    
    html.AppendLine($@"
    <div class='model-info'>
        <h3>Model: {options.ModelTitle}</h3>
        
        <div class='model-stats-grid'>
            <div class='stat-card'>
                {modelIcon}
                <div><div class='stat-value'>{analysis.FormattedModelSize}</div><div class='stat-label'>Size (sum of table sizes)</div></div>
            </div>
            <div class='stat-card'>
                {tablesIcon}
                <div><div class='stat-value'>{modelStats.TotalTables}</div><div class='stat-label'>Tables</div></div>
            </div>
            <div class='stat-card'>
                {columnsIcon}
                <div><div class='stat-value'>{modelStats.TotalColumns}</div><div class='stat-label'>Columns</div></div>
            </div>
            <div class='stat-card'>
                {measuresIcon}
                <div><div class='stat-value'>{modelStats.TotalMeasures}</div><div class='stat-label'>Measures</div></div>
            </div>
        </div>");
    
    if (analysis.TableStats?.Any() == true || analysis.ColumnStats?.Any() == true)
    {
        html.AppendLine("<div class='charts-grid'>");
        
        if (analysis.TableStats?.Any() == true)
        {
            html.AppendLine("<div class='chart-container'><h4>Top 5 Tables by Size</h4>");
            var maxTableSize = analysis.TableStats.Max(t => t.Size);
            
            foreach (var table in analysis.TableStats)
            {
                var widthPercent = maxTableSize > 0 ? (double)table.Size / maxTableSize * 100 : 0;
                html.AppendLine($@"
                <div style='margin-bottom: 10px;'>
                    <div style='display: flex; justify-content: space-between;'>
                        <span>{table.Name}</span><span>{FormatBytes(table.Size)}</span>
                    </div>
                    <div class='chart-bar'><div class='chart-fill' style='background: var(--te3-accent); width: {widthPercent:F1}%;'></div></div>
                </div>");
            }
            html.AppendLine("</div>");
        }
        
        if (analysis.ColumnStats?.Any() == true)
        {
            html.AppendLine("<div class='chart-container'><h4>Top 5 Columns by Size</h4>");
            var maxColumnSize = analysis.ColumnStats.Max(c => c.Size);
            
            foreach (var column in analysis.ColumnStats)
            {
                var widthPercent = maxColumnSize > 0 ? (double)column.Size / maxColumnSize * 100 : 0;
                html.AppendLine($@"
                <div style='margin-bottom: 10px;'>
                    <div style='display: flex; justify-content: space-between;'>
                        <span>{column.TableName}[{column.ColumnName}]</span><span>{FormatBytes(column.Size)}</span>
                    </div>
                    <div class='chart-bar'><div class='chart-fill' style='background: var(--te3-light-1); width: {widthPercent:F1}%;'></div></div>
                </div>");
            }
            html.AppendLine("</div>");
        }
        
        html.AppendLine("</div>");
    }
    
    html.AppendLine(@"
        <p style='margin: 15px 0 0 0; font-size: 14px; color: #666;'>This checklist has been automatically analyzed based on your current semantic model structure and contains pre-checked items where applicable.</p>
    </div>");
    
    return html.ToString();
}

ModelAnalysis AnalyzeCurrentModel()
{
    var analysis = new ModelAnalysis();
    
    if (Model?.Tables == null || !Model.Tables.Any())
    {
        return analysis;
    }
    
    try
    {
        var modelStats = new {
            TotalTables = Model?.Tables?.Count ?? 0,
            TotalColumns = Model?.AllColumns?.Count() ?? 0,
            TotalMeasures = Model?.AllMeasures?.Count() ?? 0
        };
        
        var tableStats = new List<(string Name, long RowCount, long Size)>();
        var columnStats = new List<(string TableName, string ColumnName, long Size)>();
        long totalModelSize = 0;
        
        try
        {
            CollectVertiPaqAnalyzerStats();
            
            // Get model size
            try
            {
                
                // Sum all table sizes
                if (totalModelSize == 0)
                {
                    foreach (var table in Model.Tables)
                    {
                        try
                        {
                            long tableSize = table.GetTotalSize();
                            totalModelSize += tableSize;
                            
                            long rowCount = 0;
                            try
                            {
                                var result = EvaluateDax($"COUNTROWS({table.DaxObjectFullName})");
                                if (result != null && long.TryParse(result.ToString(), out long rc))
                                    rowCount = rc;
                            }
                            catch { /* Ignore DAX evaluation errors */ }
                            
                            tableStats.Add((table.Name, rowCount, tableSize));
                            
                            // Get column stats for this table
                            foreach (var column in table.Columns.Where(c => c.Type != ColumnType.RowNumber))
                            {
                                try
                                {
                                    if (column is DataColumn dataColumn)
                                    {
                                        long columnSize = dataColumn.GetTotalSize();
                                        columnStats.Add((table.Name, column.Name, columnSize));
                                    }
                                }
                                catch { /* Ignore column size errors */ }
                            }
                        }
                        catch { /* Ignore table processing errors */ }
                    }
                }
                else
                {
                    foreach (var table in Model.Tables)
                    {
                        try
                        {
                            long tableSize = table.GetTotalSize();
                            
                            long rowCount = 0;
                            try
                            {
                                var result = EvaluateDax($"COUNTROWS({table.DaxObjectFullName})");
                                if (result != null && long.TryParse(result.ToString(), out long rc))
                                    rowCount = rc;
                            }
                            catch { /* Ignore DAX evaluation errors */ }
                            
                            tableStats.Add((table.Name, rowCount, tableSize));
                            
                            // Get column stats for this table
                            foreach (var column in table.Columns.Where(c => c.Type != ColumnType.RowNumber))
                            {
                                try
                                {
                                    if (column is DataColumn dataColumn)
                                    {
                                        long columnSize = dataColumn.GetTotalSize();
                                        columnStats.Add((table.Name, column.Name, columnSize));
                                    }
                                }
                                catch { /* Ignore column size errors */ }
                            }
                        }
                        catch { /* Ignore table processing errors */ }
                    }
                }
            }
            catch
            {
                // If everything fails, at least try to get basic table stats
                foreach (var table in Model.Tables)
                {
                    try
                    {
                        long tableSize = table.GetTotalSize();
                        totalModelSize += tableSize;
                        tableStats.Add((table.Name, 0, tableSize));
                    }
                    catch { /* Ignore table processing errors */ }
                }
            }
            
            analysis.HasVertiPaqStats = totalModelSize > 0;
            analysis.TotalModelSizeBytes = totalModelSize;
            analysis.FormattedModelSize = FormatBytes(totalModelSize);
            analysis.ModelStats = modelStats;
            analysis.TableStats = tableStats.OrderByDescending(t => t.Size).Take(5).ToList();
            analysis.ColumnStats = columnStats.OrderByDescending(c => c.Size).Take(5).ToList();
        }
        catch
        {
            analysis.HasVertiPaqStats = false;
            analysis.FormattedModelSize = "Unknown";
            analysis.ModelStats = modelStats;
            analysis.TableStats = new List<(string Name, long RowCount, long Size)>();
            analysis.ColumnStats = new List<(string TableName, string ColumnName, long Size)>();
        }
    }
    catch
    {
        analysis.HasVertiPaqStats = false;
    }
    
    // Continue with other analysis that doesn't require VertiPaq
    var fieldsWithDescriptions = Model.AllColumns.Count(c => !string.IsNullOrEmpty(c.Description)) +
                                Model.AllMeasures.Count(m => !string.IsNullOrEmpty(m.Description));
    analysis.HasDescriptions = fieldsWithDescriptions > 0;
    analysis.ColumnsWithDescriptionsCount = fieldsWithDescriptions;
    
    var fieldsWithFolders = Model.AllColumns.Count(c => !string.IsNullOrEmpty(c.DisplayFolder)) +
                           Model.AllMeasures.Count(m => !string.IsNullOrEmpty(m.DisplayFolder));
    analysis.HasDisplayFolders = fieldsWithFolders > 0;
    analysis.MeasuresWithFoldersCount = Model.AllMeasures.Count(m => !string.IsNullOrEmpty(m.DisplayFolder));
    
    analysis.HasMeasureTables = Model.Tables.Any(t => t.Measures.Count > 0 && t.Columns.Count <= 2);
    analysis.HasRelationships = Model.Relationships?.Any() == true;
    
    var technicalColumns = Model.AllColumns.Where(c => 
        c.Name.EndsWith("Key") || c.Name.EndsWith("ID") || c.Name.EndsWith("_ID") ||
        c.Name.StartsWith("Sys") || c.Name.Contains("Created") || c.Name.Contains("Modified") ||
        c.Name.Contains("Audit") || c.Name.Contains("Rowguid")).ToList();
    
    analysis.TechnicalColumnsCount = technicalColumns.Count;
    analysis.HasHiddenTechnicalFields = technicalColumns.Any(c => c.IsHidden);
    
    var tablesWithTechnicalNames = Model.Tables.Count(t => 
        t.Name.StartsWith("Dim") || t.Name.StartsWith("Fact") || 
        t.Name.StartsWith("View") || t.Name.Contains("_") || 
        t.Name.ToLower().Contains("staging"));
    analysis.HasCleanNaming = tablesWithTechnicalNames == 0;
    
    var nonTextColumns = Model.AllColumns.Count(c => c.DataType != DataType.String);
    analysis.HasDataTypes = nonTextColumns > Model.AllColumns.Count() * 0.3;
    
    return analysis;
}

string FormatBytes(long bytes)
{
    string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
    int suffixIndex = 0;
    double size = bytes;
    
    while (size >= 1024 && suffixIndex < suffixes.Length - 1)
    {
        size /= 1024;
        suffixIndex++;
    }
    
    return $"{size:N2} {suffixes[suffixIndex]}";
}

string EnsureSafeOutputPath(string requestedPath)
{
    try
    {
        if (!Path.IsPathRooted(requestedPath))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), requestedPath);
        }
        
        string directory = Path.GetDirectoryName(requestedPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        string testFile = Path.Combine(directory, $"test_write_{Guid.NewGuid().ToString("N")[..8]}.tmp");
        File.WriteAllText(testFile, "test");
        File.Delete(testFile);
        
        return requestedPath;
    }
    catch
    {
        string filename = Path.GetFileName(requestedPath) ?? "SemanticModel_Checklist";
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), filename);
    }
}

await Main();