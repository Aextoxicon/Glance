import React, { useState } from "react";

interface Props {
    title: string;
    count?: number;
}

export function Counter({ title, count = 0 }: Props) {
    const [value, setValue] = useState<number>(count);

    return (
        <div className="counter">
            <h1>{title}</h1>
            <button onClick={() => setValue(value + 1)}>+1</button>
            <span>{value}</span>
        </div>
    );
}
