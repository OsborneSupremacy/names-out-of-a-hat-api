# Names Out Of A Hat

An application to facilitate a "names out of hat" type gift exchange. You can try it out at [namesofouthat.com](https://namesoutofahat.com/).

![Names Out A Hat Screenshot 1](docs/demo-01.gif)

## What is a "Names Out Of A Hat" type gift exchange?

Most people probably know what this is, but I'm including a description for those who don't.

For families / groups that organize Christmas or other holiday gift exchanges, they can get expensive / excessive when everyone buys a gift for everyone else.

As a solution to that, some groups have everyone buy a gift for just one other person. That is a determined through a drawing:

1. Everyone writes their name on a piece of paper.
![Everyone writes their name on a piece of paper](docs/IMG_8224.jpg)
2. All names are put into a hat.
![All names are put into a hat](docs/IMG_8222.jpg)
3. The hat is shaken.
4. Names are drawn one at a time.
![Names are drawn one at a time](docs/IMG_8223.jpg)
5. The person drawing the name does not reveal it to anyone else.
6. If someone draws their own name (or in some variations, the name of their spouse / significant other), all names are put back in the hat, and the process is restarted.

## Why create an application to do this?

My main motivation was to be able to have the names drawn and distributed remotely. My family usually meets around Christmas Day every year. Ideally we'd know who we need to buy a gift for around Thanksgiving or earlier, but we're not always together around that time.

Here are all of the advantages that I can think of that a web application has over the paper / physical hat method:

* The drawing can be done remotely.
* A participant cannot draw their own name or the name of any _ineligible_ person (e.g. their spouse.)
* The list of ineligible recipients is flexible.
    * For groups with a small number of people, it's very possible that someone can draw the same name 2 or more years in a row. This can be prevented by marking a recipient as ineligible for certain participants.
* It is completely fair (not that fairness is a major concern when it comes to gift exchanges). Both the order in which people draw names, and the drawing of the names, is done randomly.
* Gift exchanges with complex resolutions can be found.

### Approach To Drawing Names

In most cases, each participant in the gift exchange will have the same number of eligible recipients.

For example, in my family's gift exchange, there are 3 couples. We exclude our spouses and the person whose name we drew last year. So there are 6 people, each with 3 eligible recipients.

In cases like that, there's no optimal approach as to which participant should draw first, because everyone's equal in terms of how many eligible recipients they have, and how many participants they are an eligible recipient for.

But imagine a more complex scenario, like this:

| Participant | Alpha (Eligible) | Beta (Eligible) | Charlie (Eligible) | Delta (Eligible) | Echo (Eligible) | Foxtrot (Eligible) | Golf (Eligible) | Hotel (Eligible) | India (Eligible) | Juliet (Eligible) |
| ----------- | ---------------- | --------------- | ------------------ | ---------------- | --------------- | ------------------ | --------------- | ---------------- | ---------------- | ----------------- |
| Alpha       | No               | Yes             | Yes                | Yes              | Yes             | Yes                | Yes             | Yes              | Yes              | Yes               |
| Beta        | Yes              | No              | Yes                | Yes              | Yes             | Yes                | Yes             | Yes              | Yes              | No                |
| Charlie     | Yes              | Yes             | No                 | Yes              | Yes             | Yes                | Yes             | Yes              | Yes              | No                |
| Delta       | Yes              | Yes             | Yes                | No               | Yes             | Yes                | Yes             | Yes              | Yes              | No                |
| Echo        | Yes              | Yes             | Yes                | Yes              | No              | Yes                | Yes             | Yes              | Yes              | No                |
| Foxtrot     | Yes              | Yes             | Yes                | Yes              | Yes             | No                 | Yes             | Yes              | Yes              | No                |
| Golf        | Yes              | Yes             | Yes                | Yes              | Yes             | Yes                | No              | Yes              | Yes              | No                |
| Hotel       | Yes              | Yes             | Yes                | Yes              | Yes             | Yes                | Yes             | No               | Yes              | No                |
| India       | Yes              | Yes             | Yes                | Yes              | Yes             | Yes                | Yes             | Yes              | No               | No                |
| Juliet      | Yes              | Yes             | Yes                | Yes              | Yes             | Yes                | Yes             | Yes              | Yes              | No                |

The name that should stand out is Juliet. Juliet is only an eligible recipient for Alpha. If Alpha draws any other name than Juliet, Juliet won't be picked by anyone, and the process needs to start over.

I could have tried to address that with the name picking algorithm (e.g. make it so that Alpha picks last). But I didn't want to, for the sake of maintaining randomness. It's not a huge problem if the process has to start over, since the application can keep running scenarios until the gift exchange succeeds.

So I'm taking a brute force approach to picking names. I'm not trying to find an elegant solution for difficult scenarios like the one above.

There is a limit to the number of times the application will attempt to successfully resolve a gift exchange. That limit can be increased. I don't anticipate that being necessary, however. If the application needs to handle gift exchanges that complex, it might be worth trying smarter algorithms after _n_ failed attempts with the brute force algorithm.

