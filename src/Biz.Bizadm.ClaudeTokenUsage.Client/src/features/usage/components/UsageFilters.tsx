import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { isoToKstWall } from "../periods";
import { GROUP_BY_LABELS, GROUP_BYS, UNIT_LABELS, USAGE_UNITS, type GroupBy, type UsageUnit } from "../types";
import { setGroupBy, setPeriod, setUnit, toggleFilter } from "../usageSlice";

type ListFilter = "users" | "models" | "tokenTypes";

function CheckList({ title, list, options }: { title: string; list: ListFilter; options: string[] }) {
  const dispatch = useAppDispatch();
  const selected = useAppSelector((state) => state.usage.filters[list]);
  if (options.length === 0) return null;
  return (
    <fieldset className="space-y-2">
      <legend className="text-sm font-medium">{title}</legend>
      {options.map((option) => {
        const id = `${list}-${option}`;
        return (
          <div key={option} className="flex items-center gap-2">
            <Checkbox id={id} checked={selected.includes(option)} onCheckedChange={() => dispatch(toggleFilter({ list, value: option }))} />
            <Label htmlFor={id} className="font-normal">
              {option}
            </Label>
          </div>
        );
      })}
    </fieldset>
  );
}

/** 단위·기간·나눠 볼 기준·사용자·모델·토큰 종류 선택. 기간은 단위의 보관 하한보다 이르게 고를 수 없다. */
export function UsageFilters() {
  const dispatch = useAppDispatch();
  const filters = useAppSelector((state) => state.usage.filters);
  const meta = useAppSelector((state) => state.usage.meta);
  const earliest = meta?.units.find((entry) => entry.unit === filters.unit)?.earliest;
  const min = earliest ? isoToKstWall(earliest) : undefined;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-end gap-4">
        <div className="space-y-1.5">
          <Label htmlFor="unit">단위</Label>
          <Select value={filters.unit} onValueChange={(value) => dispatch(setUnit(value as UsageUnit))}>
            <SelectTrigger id="unit" aria-label="단위" className="w-24">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {USAGE_UNITS.map((unit) => (
                <SelectItem key={unit} value={unit}>
                  {UNIT_LABELS[unit]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="from">시작 (KST)</Label>
          <Input
            id="from"
            type="datetime-local"
            value={filters.from}
            min={min}
            max={filters.to}
            onChange={(event) => event.target.value && dispatch(setPeriod({ from: event.target.value, to: filters.to }))}
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="to">끝 (KST)</Label>
          <Input
            id="to"
            type="datetime-local"
            value={filters.to}
            min={filters.from}
            onChange={(event) => event.target.value && dispatch(setPeriod({ from: filters.from, to: event.target.value }))}
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="groupBy">나눠 볼 기준</Label>
          <Select value={filters.groupBy} onValueChange={(value) => dispatch(setGroupBy(value as GroupBy))}>
            <SelectTrigger id="groupBy" aria-label="나눠 볼 기준" className="w-32">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {GROUP_BYS.map((groupBy) => (
                <SelectItem key={groupBy} value={groupBy}>
                  {GROUP_BY_LABELS[groupBy]}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <CheckList title="사용자" list="users" options={meta?.users ?? []} />
        <CheckList title="모델" list="models" options={meta?.models ?? []} />
        <CheckList title="토큰 종류" list="tokenTypes" options={meta?.tokenTypes ?? []} />
      </div>
    </div>
  );
}
