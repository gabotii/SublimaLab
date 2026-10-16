import { useState } from 'react';
import { LayersTool } from './tools/LayersTool';
import { TemplatesTool } from './tools/TemplatesTool';
import { TextTool } from './tools/TextTool';
import { UploadTool } from './tools/UploadTool';

const TABS = [
  { key: 'upload', label: 'Upload' },
  { key: 'text', label: 'Text' },
  { key: 'templates', label: 'Templates' },
  { key: 'layers', label: 'Layers' },
] as const;

type TabKey = (typeof TABS)[number]['key'];

export function ToolsPanel() {
  const [tab, setTab] = useState<TabKey>('upload');

  return (
    <>
      <h2 className="sls-panel-title">Design tools</h2>
      <div className="sls-tabs" role="tablist" aria-label="Design tools">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            role="tab"
            id={`sls-tab-${t.key}`}
            aria-selected={tab === t.key}
            aria-controls={`sls-tabpanel-${t.key}`}
            onClick={() => setTab(t.key)}
          >
            {t.label}
          </button>
        ))}
      </div>
      <div role="tabpanel" id={`sls-tabpanel-${tab}`} aria-labelledby={`sls-tab-${tab}`} className="sls-tabpanel">
        {tab === 'upload' && <UploadTool />}
        {tab === 'text' && <TextTool />}
        {tab === 'templates' && <TemplatesTool />}
        {tab === 'layers' && <LayersTool />}
      </div>
    </>
  );
}
