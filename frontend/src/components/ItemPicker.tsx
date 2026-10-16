import { Select, Typography } from 'antd';
import { useEffect, useState } from 'react';
import { useItemSearch, type ItemDto } from '../api/procurement';

/**
 * Chọn hàng hóa từ danh mục (spec §9.3 "ItemPicker"): server-side search by code, name or active ingredient, shown as
 * "Mã — Tên (ĐVT)" with the specification underneath. Used wherever a line starts from a catalog item.
 */
export function ItemPicker({
  categoryId,
  supplierId,
  exclude = [],
  accept,
  placeholder = 'Tìm hàng hóa trong danh mục theo mã, tên, hoạt chất…',
  onPick,
  style,
}: {
  categoryId?: string;
  supplierId?: string;
  /** Item ids already chosen (shown disabled). */
  exclude?: readonly string[];
  /** Extra rule on which items may be picked (e.g. not another supplier's bid item). */
  accept?: (item: ItemDto) => boolean;
  placeholder?: string;
  onPick: (item: ItemDto) => void;
  style?: React.CSSProperties;
}) {
  const [text, setText] = useState('');
  const [search, setSearch] = useState('');
  useEffect(() => {
    const t = setTimeout(() => setSearch(text.trim()), 300);
    return () => clearTimeout(t);
  }, [text]);
  const result = useItemSearch(search, { categoryId, supplierId });
  const items = (result.data?.items ?? []).filter((i) => !accept || accept(i));

  return (
    <Select<string>
      showSearch
      value={null}
      style={{ minWidth: 320, ...style }}
      placeholder={placeholder}
      filterOption={false}
      searchValue={text}
      onSearch={setText}
      loading={result.isFetching}
      notFoundContent={result.isFetching ? 'Đang tìm…' : 'Không có hàng hóa phù hợp'}
      onSelect={(id) => {
        const item = items.find((i) => i.id === id);
        if (item) onPick(item);
        setText('');
      }}
      options={items.map((i) => ({
        value: i.id,
        disabled: exclude.includes(i.id),
        label: (
          <div>
            <div>
              <span className="mono">{i.code}</span> — {i.name} ({i.unit})
            </div>
            {(i.activeIngredient || i.specification) && (
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                {[i.activeIngredient, i.specification].filter(Boolean).join(' · ')}
              </Typography.Text>
            )}
          </div>
        ),
      }))}
    />
  );
}
